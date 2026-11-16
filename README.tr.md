# Bilgisayarımı Hızlandır

[English](README.md) · **Türkçe**

**Tek düğme. Reklam yok. İhtiyacın olan hiçbir şey silinmez.**

"Bilgisayarım neden bu kadar yavaş, disk alanım nereye gitti?" diye soran herkes için küçük (100 KB'tan az) bir Windows uygulaması.

## Nasıl kullanılır

1. [Releases](../../releases) sayfasından `SpeedUpMyPC.exe` dosyasını indir.
2. Çift tıkla ve Windows izin istediğinde **Evet** de (Windows'un kendi geçici klasörlerini temizlemek için bu izne ihtiyacı var).
3. **Bilgisayarımı hızlandır** düğmesine bas.

Uygulama yeni ve imzasız olduğu için Windows **"Windows bilgisayarınızı korudu"** uyarısı gösterebilir. **Ek bilgi**'ye, sonra **Yine de çalıştır**'a tıkla. Kaynak kodun tamamı burada; ne yaptığını tam olarak kontrol edebilirsin.

## Ne yapar

- **Windows'un kendiliğinden yeniden oluşturduğu geçici dosyaları siler:** senin geçici klasörün, Windows'un geçici klasörü, eski Windows Update indirmeleri, Teslim İyileştirme önbelleği ve küçük resim önbelleği. Son 24 saatte değişen dosyalara, bir program kullanıyor olabilir diye dokunulmaz.
- **Kapatılabilecek başlangıç uygulamalarını önerir.** Spotify, Steam veya oyun başlatıcıları gibi, bilgisayar açılırken onu yavaşlatan uygulamalar. Seçimi sen yaparsın, hiçbir şey kaldırılmaz ve **Başlangıç değişikliklerini geri al** hepsini geri getirir. Görev Yöneticisi'nin Başlangıç sekmesiyle aynı şekilde çalışır.

Pencerenin altında iki araç daha var. İkisi de sadece bir şeyleri **gösterir**:

- **Kopya ve büyük dosyaları bul.** Seçtiğin klasörlerde (başlangıçta Masaüstü, Belgeler, İndirilenler, Resimler, Videolar ve Müzik) içeriği birebir aynı olan dosyaları ve uzun süredir değişmemiş büyük dosyaları arar. Ne kadar büyük ve ne kadar eski olacağını sen belirlersin. Hiçbir dosyayı silmez, taşımaz veya değiştirmez: **Klasörde göster** Explorer'ı açar, kararı sen verirsin. Bağlantılar, junction'lar ve yalnızca bulutta duran OneDrive dosyaları atlanır; içlerine girilmez, indirilmez. Açamadığı her şey nedeniyle birlikte **Atlananlar** altında listelenir.
- **Yüklü uygulamalar.** Uygulamalarını boyutlarıyla listeler. Boyutunu bildirmeyen bir uygulamanın kurulum klasörünü kendisi ölçer. Hangi uygulamaları az kullandığını göstermez, çünkü Windows masaüstü uygulamaları için güvenilir bir "son kullanım" tarihi tutmuyor ve uygulama tahmin yürütmez. **Windows Ayarlar'da kaldır**, Windows'un kendi Yüklü uygulamalar sayfasını açar ve arama kutusu için uygulamanın adını kopyalar. Kaldırma işlemi orada yapılır.

Uygulama Türkçe ve İngilizce konuşur ve Windows'un dilini kendisi seçer. Windows'un açık veya koyu modunu da takip eder. Kendin seçmek istersen sağ alttaki **Ayarlar**'ı aç.

## Asla yapmadıkları

- Belgelerine, fotoğraflarına, İndirilenler klasörüne, Geri Dönüşüm Kutusu'na veya tarayıcı verilerine asla dokunmaz.
- Bulduğu kopyaları veya büyük dosyaları asla silmez ve hiçbir uygulamayı kaldırmaz. Sadece listeler.
- Sürücülere, virüs korumasına veya tanımadığı hiçbir başlangıç uygulamasına asla dokunmaz. Yalnızca [kısa ve okunabilir bir listedeki](src/SpeedUpMyPC/SafeStartupList.cs) uygulamalar önerilir.
- Reklam yok, takip yok, internet bağlantısı yok, kurulum yok.

## Kendin derle

```powershell
./build.ps1
```

SDK gerekmez. Windows ile birlikte gelen C# derleyicisini kullanır. Uygulama `bin/SpeedUpMyPC.exe` olarak oluşur.

## Dil ekle

1. [`src/SpeedUpMyPC/lang/en.txt`](src/SpeedUpMyPC/lang/en.txt) dosyasını dil koduyla adlandırılmış yeni bir dosyaya kopyala, örneğin `de.txt` veya `pt-BR.txt`.
2. Her `=` işaretinden sonraki metni ve `@name` satırını çevir. `{0}`, `{1}` ve `\n` olduğu gibi kalsın.
3. Derle. Yeni dil **Ayarlar**'da görünür ve Windows o dildeyse kendiliğinden seçilir. Çevirmediğin satırlar İngilizce görünür.

## Lisans

MIT
