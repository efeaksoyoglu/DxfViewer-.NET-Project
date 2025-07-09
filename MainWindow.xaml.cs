using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using Path = System.Windows.Shapes.Path;
using System.Text;

namespace DxfViewerWithIxMilia
{
    public partial class MainWindow : Window
    {
        private List<(double x, double y, int kalipNo)> kalipMerkezUzakliklari = new List<(double x, double y, int kalipNo)>();

        private double PozitifSifirYap(double deger)
        {
            // -0 değerini 0'a çevirir, diğer değerleri değiştirmez
            return deger == 0 ? 0 : deger;
        }

        private string currentDxfFilePath = null;


        public MainWindow()
        {
            InitializeComponent();
        }

        private void btnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "DXF dosyaları (*.dxf)|*.dxf";

            if (openFileDialog.ShowDialog() == true)
            {
                string fileName = System.IO.Path.GetFileName(openFileDialog.FileName);
                SecilenDosyaTextBlock.Text = $"Seçilen dosya: {fileName}";
                currentDxfFilePath = openFileDialog.FileName;

                try
                {
                    var dxf = DxfFile.Load(openFileDialog.FileName);
                    drawingCanvas.Children.Clear();
                    kalipMerkezUzakliklari.Clear(); // Önceki kalıp merkez uzaklıklarını temizle

                    var circles = dxf.Entities.OfType<DxfCircle>().ToList();
                    var lines = dxf.Entities.OfType<DxfLine>().ToList();
                    var arcs = dxf.Entities.OfType<DxfArc>().ToList();
                    var lwPolylines = dxf.Entities.OfType<DxfLwPolyline>().ToList();

                    // Elips oluşturmak için kullanılan ARC merkezlerini tespit et
                    var elipsArcMerkezler = new HashSet<(double x, double y)>();
                    var arcGroupsByLayer = arcs.GroupBy(a => a.Layer).ToDictionary(g => g.Key, g => g.ToList());
                    foreach (var layerKey in arcGroupsByLayer.Keys)
                    {
                        var layerArcs = arcGroupsByLayer[layerKey];
                        var islenenArclar = new HashSet<DxfArc>();
                        foreach (var arc1 in layerArcs)
                        {
                            if (islenenArclar.Contains(arc1)) continue;
                            DxfArc enYakinArc = null;
                            double enKucukMesafe = double.MaxValue;
                            foreach (var arc2 in layerArcs)
                            {
                                if (arc1 == arc2) continue;
                                if (islenenArclar.Contains(arc2)) continue;
                                if (Math.Abs(arc1.Radius - arc2.Radius) > 0.001) continue;
                                if (Math.Abs(arc1.Center.Y - arc2.Center.Y) > 5.0) continue;
                                double mesafe = Math.Abs(arc1.Center.X - arc2.Center.X);
                                if (mesafe < enKucukMesafe && mesafe > 1.0)
                                {
                                    enKucukMesafe = mesafe;
                                    enYakinArc = arc2;
                                }
                            }
                            if (enYakinArc != null)
                            {
                                elipsArcMerkezler.Add((Math.Round(arc1.Center.X, 2), Math.Round(arc1.Center.Y, 2)));
                                elipsArcMerkezler.Add((Math.Round(enYakinArc.Center.X, 2), Math.Round(enYakinArc.Center.Y, 2)));
                                islenenArclar.Add(arc1);
                                islenenArclar.Add(enYakinArc);
                            }
                        }
                    }

                    if (!circles.Any() && !lines.Any() && !arcs.Any() && !lwPolylines.Any())
                    {
                        MessageBox.Show("DXF dosyasında çizilecek tanınan şekil bulunamadı.", "Bilgi");
                        return;
                    }

                    // Merkezi Referans Noktasını Hesapla
                    var (merkez_x, merkez_y) = HesaplaMerkezReferansNoktasi(dxf);

                    // Tüm entity'lerin sınırlarını bulmak için
                    var allEntities = circles.Select(c => new
                    {
                        X1 = c.Center.X - c.Radius,
                        X2 = c.Center.X + c.Radius,
                        Y1 = c.Center.Y - c.Radius,
                        Y2 = c.Center.Y + c.Radius
                    }).Concat(lines.Select(l => new
                    {
                        X1 = l.P1.X,
                        X2 = l.P2.X,
                        Y1 = l.P1.Y,
                        Y2 = l.P2.Y
                    })).Concat(arcs.Select(a => new
                    {
                        X1 = a.Center.X - a.Radius,
                        X2 = a.Center.X + a.Radius,
                        Y1 = a.Center.Y - a.Radius,
                        Y2 = a.Center.Y + a.Radius
                    })).Concat(lwPolylines.SelectMany(pl =>
                        pl.Vertices.Select(v => new
                        {
                            X1 = v.X,
                            X2 = v.X,
                            Y1 = v.Y,
                            Y2 = v.Y
                        }))).ToList();

                    var minX = allEntities.Min(e => Math.Min(e.X1, e.X2));
                    var minY = allEntities.Min(e => Math.Min(e.Y1, e.Y2));
                    var maxX = allEntities.Max(e => Math.Max(e.X1, e.X2));
                    var maxY = allEntities.Max(e => Math.Max(e.Y1, e.Y2));

                    double canvasWidth = maxX - minX;
                    double canvasHeight = maxY - minY;

                    drawingCanvas.Width = canvasWidth + 40;
                    drawingCanvas.Height = canvasHeight + 40;

                    // --- MANUEL KOORDİNAT EKSENİ VAR MI KONTROLÜ ---
                    bool hasManualAxis = lines.Any(l => IsAxisLine(l, dxf));
                    if (!hasManualAxis)
                        DrawCoordinateAxis(drawingCanvas, 30, canvasHeight + 10, 80, 80);

                    // --- LAYERLARA GÖRE KALIPLAR: HER KALIPTA EN SOL+ALT SHAPE İÇİN --- 
                    var layerMinPoint = new Dictionary<string, Point>();

                    // Her layer'daki tüm entity'lerin tüm noktalarını kontrol et
                    foreach (var layer in dxf.Entities.Select(e => e.Layer).Distinct())
                    {
                        double bestX = double.MaxValue;
                        double bestY = double.MaxValue;
                        foreach (var ent in dxf.Entities.Where(e => e.Layer == layer))
                        {
                            foreach (var pt in GetEntityPoints(ent))
                            {
                                if (pt.X < bestX || (Math.Abs(pt.X - bestX) < 1e-6 && pt.Y < bestY))
                                {
                                    bestX = pt.X;
                                    bestY = pt.Y;
                                }
                            }
                        }
                        if (bestX < double.MaxValue && bestY < double.MaxValue)
                            layerMinPoint[layer] = new Point(bestX - minX + 20, canvasHeight - (bestY - minY) + 20);
                    }

                    // --- CIRCLE ---
                    foreach (var circle in circles)
                    {
                        var color = GetEntityColor(circle, dxf);
                        var ellipse = new Ellipse
                        {
                            Width = circle.Radius * 2,
                            Height = circle.Radius * 2,
                            Stroke = new SolidColorBrush(color),
                            StrokeThickness = 2
                        };
                        double left = circle.Center.X - circle.Radius - minX + 20;
                        double top = canvasHeight - (circle.Center.Y + circle.Radius - minY) + 20;
                        Canvas.SetLeft(ellipse, left);
                        Canvas.SetTop(ellipse, top);
                        drawingCanvas.Children.Add(ellipse);

                        // Circle kalıp merkezini hesapla
                        int kalipNo = GetKalipNumberIntFromLayerName(circle.Layer);
                        if (kalipNo > 0)
                        {
                            // Sadece elipsArcMerkezler setinde YOKSA ekle
                            var merkezTuple = (Math.Round(circle.Center.X, 2), Math.Round(circle.Center.Y, 2));
                            if (!elipsArcMerkezler.Contains(merkezTuple))
                            {
                                double xUzaklik = PozitifSifirYap(Math.Round(circle.Center.X - merkez_x, 2));
                                double yUzaklik = PozitifSifirYap(Math.Round(circle.Center.Y - merkez_y, 2));
                                kalipMerkezUzakliklari.Add((xUzaklik, yUzaklik, kalipNo));
                            }
                        }

                    }

                    // --- LINES ---
                    foreach (var line in lines)
                    {
                        var color = GetEntityColor(line, dxf);
                        var lineShape = new Line
                        {
                            X1 = line.P1.X - minX + 20,
                            Y1 = canvasHeight - (line.P1.Y - minY) + 20,
                            X2 = line.P2.X - minX + 20,
                            Y2 = canvasHeight - (line.P2.Y - minY) + 20,
                            Stroke = new SolidColorBrush(color),
                            StrokeThickness = 2
                        };
                        drawingCanvas.Children.Add(lineShape);
                    }

                    // --- ARCS ---
                    foreach (var arc in arcs)
                    {
                        var color = GetEntityColor(arc, dxf);
                        var path = CreateArcPath(arc, minX, minY, canvasHeight, color);
                        drawingCanvas.Children.Add(path);
                    }

                    // --- LWPOLYLINE ---
                    foreach (var poly in lwPolylines)
                    {
                        var color = GetEntityColor(poly, dxf);
                        var polyline = new Polyline
                        {
                            Stroke = new SolidColorBrush(color),
                            StrokeThickness = 2
                        };
                        var points = new PointCollection();
                        foreach (var vertex in poly.Vertices)
                        {
                            double x = vertex.X - minX + 20;
                            double y = canvasHeight - (vertex.Y - minY) + 20;
                            points.Add(new Point(x, y));
                        }
                        if (poly.IsClosed && points.Count > 0)
                            points.Add(points[0]);
                        polyline.Points = points;
                        drawingCanvas.Children.Add(polyline);
                    }

                    // --- KALIP NUMARALARINI GÖSTER: EN SOL + EN ALT SHAPE NOKTASINA ---
                    foreach (var kvp in layerMinPoint)
                    {
                        string layerName = kvp.Key;
                        string kalipNo = GetKalipNumberFromLayerName(layerName);
                        if (!string.IsNullOrEmpty(kalipNo))
                        {
                            var textBlock = new TextBlock
                            {
                                Text = $"{kalipNo}.K",
                                FontSize = 22,
                                FontWeight = FontWeights.Bold,
                                Foreground = Brushes.Black
                            };
                            Canvas.SetLeft(textBlock, kvp.Value.X - 15);
                            Canvas.SetTop(textBlock, kvp.Value.Y + 8);
                            drawingCanvas.Children.Add(textBlock);
                        }
                    }

                    // --- ELİPS KALIP HESAPLAMA (ARC ÇİFTLERİ) ---
                    HesaplaElipsKalipMerkezleri(arcs, dxf, merkez_x, merkez_y);

                    // Merkezi Referans Noktasını Pop-up ile Göster
                    MessageBox.Show($"Çizimin merkezi referans noktası = x: {merkez_x.ToString("F3")}, y: {merkez_y.ToString("F3")}",
                                   "Merkez Referans Noktası",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);

                    // Merkezi Referans Noktasını Canvas üzerinde göster
                    DrawReferencePoint(drawingCanvas, merkez_x - minX + 20, canvasHeight - (merkez_y - minY) + 20);

                    // Kalıp Merkez Uzaklıklarını Sırala ve Göster
                    GosterKalipMerkezUzakliklari();

                    // Kalıpların merkez koordinatlarını hesapla ve göster
                    /* HesaplaVeGosterKalipMerkezKoordinatlari(circles, arcs, dxf); */

                    MessageBox.Show($"{circles.Count} daire, {lines.Count} çizgi, {arcs.Count} yay, {lwPolylines.Count} lwpolyline çizildi.", "Çizim Tamam");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnExportMeo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SecilenDosyaTextBlock.Text) || SecilenDosyaTextBlock.Text == "Seçilen dosya: -")
            {
                MessageBox.Show("Önce bir .dxf dosyası açmalısınız!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dxf = DxfFile.Load(currentDxfFilePath);

            // 1. Dosya adı (uzantısız) ve gerçek dosya adı
            string fileName = SecilenDosyaTextBlock.Text.Replace("Seçilen dosya: ", "").Trim();
            string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileName);

            // 2. Dikdörtgen çerçeve kenarlarını yeniden bul
           // var dxf = DxfFile.Load(System.IO.Path.Combine(Environment.CurrentDirectory, fileName));
            var dikdortgenKenarlar = dxf.Entities.OfType<DxfLine>()
                .Where(line => {
                    var color = GetEntityColor(line, dxf);
                    return line.Layer == "0" && color == Colors.White;
                })
                .ToList();

            if (dikdortgenKenarlar.Count < 4)
            {
                MessageBox.Show("Çizimde dikdörtgen çerçeve tespit edilemedi. Dışa aktarılamaz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 3. Uzun kenar ve kısa kenar tespiti (üst/alt ve sol/sağ)
            double uzunluk = 0, kalinlik = 0;

            // -- Kısa kenar: dikey kenar (X sabit, Y farklı)
            var dikeyKenarlar = dikdortgenKenarlar.Where(line => Math.Abs(line.P1.X - line.P2.X) < 0.001).ToList();
            if (dikeyKenarlar.Count >= 1)
            {
                var kisaKenar = dikeyKenarlar[0]; // ilkini seçmek yeterli
                kalinlik = Math.Abs(kisaKenar.P1.Y - kisaKenar.P2.Y);
            }

            // -- Uzun kenar: yatay kenar (Y sabit, X farklı)
            var yatayKenarlar = dikdortgenKenarlar.Where(line => Math.Abs(line.P1.Y - line.P2.Y) < 0.001).ToList();
            if (yatayKenarlar.Count >= 1)
            {
                var uzunKenar = yatayKenarlar[0];
                uzunluk = Math.Abs(uzunKenar.P1.X - uzunKenar.P2.X);
            }

            // Nokta ile formatla (ondalıklı sayı)
            string uzunlukStr = uzunluk.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            string kalinlikStr = kalinlik.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            // 4. Uzaklıklar (x'e göre sıralı, virgülsüz, tek satır)
            var siraliUzakliklar = kalipMerkezUzakliklari.OrderBy(k => k.x).ThenBy(k => k.y).ToList();
            var uzaklikStrs = siraliUzakliklar.Select(k =>
                $"x: {k.x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} y: {k.y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} k: {k.kalipNo}"
            );
            string uzakliklar = string.Join(" ", uzaklikStrs);

            // --- BAĞIMSIZ İÇ LINE’LARI EKLEME ---
            var iceridekiLineGruplari = dxf.Entities.OfType<DxfLine>()
                .Where(line =>
                    line.Layer != "0" && // Dikdörtgen çerçeve olmasın
                    GetEntityColor(line, dxf) != Colors.White && // Beyaz olmasın
                    GetEntityColor(line, dxf) != GetColorFromACI(40) // Elips line’ı olmasın (renk 40)
                )
                .GroupBy(line => line.Layer)
                .ToList();

            string ekIcerik = "";
            foreach (var grup in iceridekiLineGruplari)
            {
                var linesInGroup = grup.ToList();
                if (linesInGroup.Count == 2)
                {
                    // Genişlik (uzunluk): Herhangi bir line’daki X1 ve X2’nin farkı (mutlak değer)
                    double genislik = Math.Abs(linesInGroup[0].P1.X - linesInGroup[0].P2.X);

                    // Kalınlık: İki line’ın Y değerlerinin farkı (ikisinin Y’sinin ortalaması alınır, sonra aradaki fark)
                    double y1 = (linesInGroup[0].P1.Y + linesInGroup[0].P2.Y) / 2.0;
                    double y2 = (linesInGroup[1].P1.Y + linesInGroup[1].P2.Y) / 2.0;
                    double kalinlik2 = Math.Abs(y1 - y2);

                    // String’e ekle, noktadan sonraki iki hane, her zaman noktayı ayraç yap
                    string genislikStr = genislik.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    string kalinlikStr2 = kalinlik2.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

                    ekIcerik += $" {genislikStr} {kalinlikStr2}";
                }
            }


            // 5. Satır: <dosyaadı> <uzunluk> <kalınlık> <uzaklıklar>
            string meoSatir = $"{fileNameWithoutExt} {uzunlukStr} {kalinlikStr}{ekIcerik} {uzakliklar}";


            // 6. Dosyayı kaydet
            var saveDialog = new Microsoft.Win32.SaveFileDialog()
            {
                Filter = "MEO dosyası (*.meo)|*.meo",
                FileName = fileNameWithoutExt + ".meo"
            };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    System.IO.File.WriteAllText(saveDialog.FileName, meoSatir);
                    MessageBox.Show(".meo dosyası başarıyla kaydedildi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Dosya kaydedilemedi: " + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void HesaplaElipsKalipMerkezleri(List<DxfArc> arcs, DxfFile dxf, double merkez_x, double merkez_y)
        {
            // Arc'ları layer'a göre grupla
            var arcGroupsByLayer = arcs.GroupBy(a => a.Layer).ToDictionary(g => g.Key, g => g.ToList());

            // Her layer grubu için
            foreach (var layerKey in arcGroupsByLayer.Keys)
            {
                // Bu layer'daki kalıp numarasını al
                int kalipNo = GetKalipNumberIntFromLayerName(layerKey);
                if (kalipNo <= 0) continue; // Geçerli bir kalıp numarası değilse atla

                var layerArcs = arcGroupsByLayer[layerKey];

                // İşlenmiş arc'ları takip etmek için set
                var islenenArclar = new HashSet<DxfArc>();

                // Her arc için
                foreach (var arc1 in layerArcs)
                {
                    // Bu arc zaten işlenmişse atla
                    if (islenenArclar.Contains(arc1)) continue;

                    // Bu arc için en yakın ve uygun eşini bul
                    DxfArc enYakinArc = null;
                    double enKucukMesafe = double.MaxValue;

                    foreach (var arc2 in layerArcs)
                    {
                        // Kendisi olamaz
                        if (arc1 == arc2) continue;

                        // Zaten işlenmiş arc'ı atla
                        if (islenenArclar.Contains(arc2)) continue;

                        // İki arc'ın yarıçapları yaklaşık olarak aynı olmalı
                        if (Math.Abs(arc1.Radius - arc2.Radius) > 0.001) continue;

                        // İki arc'ın Y değerleri yaklaşık olarak aynı olmalı
                        if (Math.Abs(arc1.Center.Y - arc2.Center.Y) > 5.0) continue;

                        // İki arc arasındaki X mesafesini hesapla
                        double mesafe = Math.Abs(arc1.Center.X - arc2.Center.X);

                        // Eğer bu mesafe şimdiye kadarki en küçük mesafeden küçükse, güncelle
                        if (mesafe < enKucukMesafe && mesafe > 1.0)  // Minimum mesafe kontrolü
                        {
                            enKucukMesafe = mesafe;
                            enYakinArc = arc2;
                        }
                    }

                    // Eğer en yakın arc bulunduysa, elips kalıbı oluştur
                    if (enYakinArc != null)
                    {
                        // Elips kalıbının merkez noktasını hesapla
                        double kalipMerkezX = (arc1.Center.X + enYakinArc.Center.X) / 2;
                        double kalipMerkezY = (arc1.Center.Y + enYakinArc.Center.Y) / 2;

                        // Merkez noktasının referans noktasına olan uzaklığını hesapla
                        double xUzaklik = PozitifSifirYap(Math.Round(kalipMerkezX - merkez_x, 2));
                        double yUzaklik = PozitifSifirYap(Math.Round(kalipMerkezY - merkez_y, 2));

                        // Kalıp merkez uzaklıklarına ekle
                        kalipMerkezUzakliklari.Add((xUzaklik, yUzaklik, kalipNo));

                        // Bu arc'ları işlenmiş olarak işaretle
                        islenenArclar.Add(arc1);
                        islenenArclar.Add(enYakinArc);
                    }
                }
            }
        }

        private void GosterKalipMerkezUzakliklari()
        {
            var siraliUzakliklar = kalipMerkezUzakliklari
                .OrderBy(k => k.x)
                .ThenBy(k => k.y)
                .ToList();

            StringBuilder sb = new StringBuilder();
            int sayac = 0;

            foreach (var uzaklik in siraliUzakliklar)
            {
                // Formatı "x: 30 y: -0 k: 1" şeklinde oluştur
                sb.Append($"x: {uzaklik.x} y: {uzaklik.y} k: {uzaklik.kalipNo}");

                sayac++;

                // Her 5 kalıptan sonra yeni satır, değilse virgül ve boşluk
                if (sayac % 5 == 0)
                {
                    sb.AppendLine();
                }
                else if (sayac < siraliUzakliklar.Count)
                {
                    sb.Append(", ");
                }
            }

            KalipUzaklikTextBlock.Text = sb.ToString();
        }

        // --- Layer adından kalıp numarasını int olarak çek ---
        private int GetKalipNumberIntFromLayerName(string layerName)
        {
            string numStr = GetKalipNumberFromLayerName(layerName);
            if (int.TryParse(numStr, out int num))
                return num;
            return 0;
        }

        // --- Merkezi Referans Noktasını Canvas üzerinde çiz ---
        private void DrawReferencePoint(Canvas canvas, double x, double y)
        {
            // Merkezi noktayı gösteren içi dolu küçük kırmızı daire
            var circle = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Red
            };
            Canvas.SetLeft(circle, x - 5); // Dairenin merkezi x,y noktasında olacak şekilde konumlandır
            Canvas.SetTop(circle, y - 5);

            canvas.Children.Add(circle);
        }

        // --- Merkezi Referans Noktasını Hesaplama Fonksiyonu ---
        private (double x, double y) HesaplaMerkezReferansNoktasi(DxfFile dxf)
        {
            try
            {
                // Dikdörtgen çerçevenin kenarlarını tespit et (Layer = "0" VE Renk = 7/Beyaz)
                var dikdörtgenKenarları = dxf.Entities.OfType<DxfLine>()
                    .Where(line => {
                        var color = GetEntityColor(line, dxf);
                        return line.Layer == "0" && color == Colors.White;
                    })
                    .ToList();

                if (dikdörtgenKenarları.Count < 4)
                {
                    // Eğer yeterli kenar bulunamadıysa, varsayılan bir değer döndür
                    return (0, 0);
                }

                // Dikdörtgenin dikey kenarlarını bul (bir x değeri, iki y değeri)
                var dikeyKenarlar = dikdörtgenKenarları
                    .Where(line => Math.Abs(line.P1.X - line.P2.X) < 0.001) // x değerleri aynı (dikey çizgi)
                    .ToList();

                if (dikeyKenarlar.Count < 2)
                {
                    // Eğer dikey kenar bulunamadıysa, varsayılan bir değer döndür
                    return (0, 0);
                }

                // Dikey kenarlardan x değeri en küçük olanı sol kenar olarak seç
                DxfLine solKenar = dikeyKenarlar[0];
                foreach (var kenar in dikeyKenarlar)
                {
                    if (kenar.P1.X < solKenar.P1.X)
                    {
                        solKenar = kenar;
                    }
                }

                // Referans noktasını hesapla
                double x = solKenar.P1.X; // Sol kenarın x değeri
                double y = (solKenar.P1.Y + solKenar.P2.Y) / 2; // y değerlerinin ortalaması

                return (x, y);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Merkez referans noktası hesaplanırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return (0, 0);
            }
        }

        // --- Manuel koordinat ekseni çizimi olup olmadığını bul ---
        private bool IsAxisLine(DxfLine line, DxfFile dxf)
        {
            // X, Y veya Z eksenine benzer olup olmadığını tespit etmek için basit mantık
            var color = GetEntityColor(line, dxf);
            var length = Math.Sqrt(Math.Pow(line.P2.X - line.P1.X, 2) + Math.Pow(line.P2.Y - line.P1.Y, 2));
            if (length < 10) return false;
            // Renk veya yön kontrolü eklenebilir
            if (color == Colors.Red || color == Colors.Green || color == Colors.Blue) return true;
            return false;
        }

        // --- Entity'yi oluşturan tüm noktaları döndürür ---
        private IEnumerable<(double X, double Y)> GetEntityPoints(DxfEntity entity)
        {
            if (entity is DxfLine l)
            {
                yield return (l.P1.X, l.P1.Y);
                yield return (l.P2.X, l.P2.Y);
            }
            else if (entity is DxfCircle c)
            {
                yield return (c.Center.X + c.Radius, c.Center.Y);
                yield return (c.Center.X - c.Radius, c.Center.Y);
                yield return (c.Center.X, c.Center.Y + c.Radius);
                yield return (c.Center.X, c.Center.Y - c.Radius);
            }
            else if (entity is DxfArc a)
            {
                yield return (a.Center.X + a.Radius, a.Center.Y);
                yield return (a.Center.X - a.Radius, a.Center.Y);
                yield return (a.Center.X, a.Center.Y + a.Radius);
                yield return (a.Center.X, a.Center.Y - a.Radius);
            }
            else if (entity is DxfLwPolyline p)
            {
                foreach (var v in p.Vertices)
                    yield return (v.X, v.Y);
            }
        }

        // --- AutoCAD renk kodu tablosu ---
        public static Color GetColorFromACI(short aci)
        {
            if (aci < 1 || aci > 255)
                return Colors.Black;

            switch (aci)
            {
                case 1: return Colors.Red;
                case 2: return Colors.Yellow;
                case 3: return Colors.Green;
                case 4: return Colors.Cyan;
                case 5: return Colors.Blue;
                case 6: return Colors.Magenta;
                case 7: return Colors.White;
                case 8: return Color.FromRgb(65, 65, 65);
                case 9: return Colors.LightGray;
                case 30: return Color.FromRgb(255, 127, 0);
                case 31: return Color.FromRgb(255, 212, 170);
                case 40: return Color.FromRgb(255, 191, 0);
            }

            // ACI 10–255 için(30,31 ve 40 hariç) sistematik renk üretimi
            byte r = (byte)(50 + (aci * 40) % 206);
            byte g = (byte)(30 + (aci * 80) % 226);
            byte b = (byte)(80 + (aci * 60) % 176);

            return Color.FromRgb(r, g, b);
        }

        // -- Her entity tipi için overload --
        private Color GetEntityColor(DxfCircle entity, DxfFile dxf)
        {
            var color = entity.Color;
            if (color.IsByLayer)
            {
                var layer = dxf.Layers.FirstOrDefault(l => l.Name == entity.Layer);
                if (layer != null)
                    color = layer.Color;
            }
            return GetColorFromACI(color.Index);
        }
        private Color GetEntityColor(DxfLine entity, DxfFile dxf)
        {
            var color = entity.Color;
            if (color.IsByLayer)
            {
                var layer = dxf.Layers.FirstOrDefault(l => l.Name == entity.Layer);
                if (layer != null)
                    color = layer.Color;
            }
            return GetColorFromACI(color.Index);
        }
        private Color GetEntityColor(DxfArc entity, DxfFile dxf)
        {
            var color = entity.Color;
            if (color.IsByLayer)
            {
                var layer = dxf.Layers.FirstOrDefault(l => l.Name == entity.Layer);
                if (layer != null)
                    color = layer.Color;
            }
            return GetColorFromACI(color.Index);
        }
        private Color GetEntityColor(DxfLwPolyline entity, DxfFile dxf)
        {
            var color = entity.Color;
            if (color.IsByLayer)
            {
                var layer = dxf.Layers.FirstOrDefault(l => l.Name == entity.Layer);
                if (layer != null)
                    color = layer.Color;
            }
            return GetColorFromACI(color.Index);
        }

        // Layer adından kalıp numarasını çek ("Kalıp6" -> "6")
        private string GetKalipNumberFromLayerName(string layerName)
        {
            if (layerName != null && layerName.ToLower().StartsWith("kalıp"))
            {
                string s = new string(layerName.Where(char.IsDigit).ToArray());
                return s;
            }
            return "";
        }

        // --- Koordinat ekseni çizimi ---
        private void DrawCoordinateAxis(Canvas canvas, double x, double y, double lengthX, double lengthY)
        {
            // X ekseni
            var xAxis = new Line
            {
                X1 = x,
                Y1 = y,
                X2 = x + lengthX,
                Y2 = y,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            // Y ekseni
            var yAxis = new Line
            {
                X1 = x,
                Y1 = y,
                X2 = x,
                Y2 = y - lengthY,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            canvas.Children.Add(xAxis);
            canvas.Children.Add(yAxis);

            // Ok başları
            var arrowX1 = new Line
            {
                X1 = x + lengthX,
                Y1 = y,
                X2 = x + lengthX - 10,
                Y2 = y - 7,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            var arrowX2 = new Line
            {
                X1 = x + lengthX,
                Y1 = y,
                X2 = x + lengthX - 10,
                Y2 = y + 7,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            var arrowY1 = new Line
            {
                X1 = x,
                Y1 = y - lengthY,
                X2 = x - 7,
                Y2 = y - lengthY + 10,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            var arrowY2 = new Line
            {
                X1 = x,
                Y1 = y - lengthY,
                X2 = x + 7,
                Y2 = y - lengthY + 10,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            canvas.Children.Add(arrowX1);
            canvas.Children.Add(arrowX2);
            canvas.Children.Add(arrowY1);
            canvas.Children.Add(arrowY2);
        }

        // --- Arc çizimi için ---
        private Path CreateArcPath(DxfArc arc, double minX, double minY, double canvasHeight, Color color)
        {
            var startAngleRad = arc.StartAngle * Math.PI / 180.0;
            var endAngleRad = arc.EndAngle * Math.PI / 180.0;
            var startX = arc.Center.X + arc.Radius * Math.Cos(startAngleRad);
            var startY = arc.Center.Y + arc.Radius * Math.Sin(startAngleRad);
            var endX = arc.Center.X + arc.Radius * Math.Cos(endAngleRad);
            var endY = arc.Center.Y + arc.Radius * Math.Sin(endAngleRad);

            var arcSegment = new ArcSegment
            {
                Point = new Point(endX - minX + 20, canvasHeight - (endY - minY) + 20),
                Size = new Size(arc.Radius, arc.Radius),
                SweepDirection = SweepDirection.Counterclockwise,
                IsLargeArc = Math.Abs(arc.EndAngle - arc.StartAngle) > 180
            };

            var figure = new PathFigure
            {
                StartPoint = new Point(startX - minX + 20, canvasHeight - (startY - minY) + 20),
                Segments = { arcSegment }
            };

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return new Path
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2,
                Data = geometry
            };
        }
    }
}