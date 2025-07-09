DxfViewerWithIxMilia
WPF (C#) ile geliştirilmiş, DXF dosyalarını görselleştiren ve ölçüm hesaplayan gelişmiş bir masaüstü uygulaması

🚀 Özellikler
✅ .dxf formatındaki teknik çizimleri yükleyip gerçek zamanlı görselleştirme
✅ Çizim içindeki çember, çizgi, yay ve polyline gibi tüm temel entity’leri renkli ve katman bilgileriyle birlikte render etme
✅ Çizimden otomatik olarak referans merkezi algılama ve görselde işaretleme
✅ Kalıp merkezlerini (çember veya elips şeklinde) tespit etme ve bunların referans merkezine olan X ve Y eksenindeki mesafelerini hesaplama
✅ Hesaplanan mesafeleri UI üzerinde listeleme ve .txt veya .meo formatında dışa aktarma
✅ Kalıp uzunluk ve kalınlık gibi bilgilerinin hesaplanması ve dosya adıyla birlikte export edilmesi
✅ Modern ve kullanıcı dostu WPF arayüzü

⚙️ Teknik Detaylar
Platform: .NET 8.0 (self-contained publish)
Arayüz: WPF (.NET 8.0)
DXF parsing kütüphanesi: IxMilia.Dxf
Dil: C#

NOT: Bu projeyi tamamen müşterilerimin özel ihtiyaçlarına göre spesifik olarak geliştirdim. Yani uygulamanın içerisinde yapılan hesaplamalar, kalıp renklendirmeleri, merkezi referans noktası hesaplanması, manuel koordinat ekseni kontrolü gibi bir çok özellik müşterilerimin 
kullanmış oldukları .dxf dosyalarının içeriklerine göre özelleştirildi ve uygulamada kodlandı. 
Dolayısıyla bu uygulama tamamen kişisel ihtiyaçlar doğrultusunda özelleştirilen ve BÜTÜN .DXF DOSYALARI İÇİN AYNI ŞEKİLDE ÇALIŞAN BİR UYGULAMA DEĞİLDİR. 
Ancak, kodlarımı kullanarak kendi ihtiyaçlarınıza göre üzerinde istediğiniz değişiklikleri kolay ve anlaşılabilir mimari yapım sayesinde yapabilir ve kendinize göre uygulamayı uyarlayabilirsiniz. 
Bu uygulamanın geliştirme aşamalarında Claude Code Pro(Çoğunlukla 3.5 Sonnet'i kullansam da zaman zaman 4.0 modelini de kullandım) ve GPT Plus(O an ki ihtiyacımın gereksinimine göre farklı model çeşitleri arasında seçimler yaptım ancak en çok kullandığım modeller 4o, 4.1 ve 4.5 modelleriydi) yapay zeka modellerini çok sık kullandım. Çünkü ben projeye başlamadan önce değişken tanımlamaları hariç C# dilinde hiç bir bilgiye sahip değildim ve daha önce hiç bir projede .NET framework'ünü de kullanmamıştım. Ama bu proje hem benim ilk freelance projem olması açısından hem de projeyi geliştirdiğim süreç boyunca özellikle hakim olmadığım bir programlama dilinde ve çerçevesinde yapay zeka kullanarak nasıl bir iş çıkarttığımı gözlemleyebilmem ve en önemlisi de müşterilerimin beklentilerini karşılayabilmem açısından benim için çok değerli.
