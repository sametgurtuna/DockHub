/**
 * DockHub 1 haftalık UX geri bildirim formunu oluşturur.
 * Kullanım: script.google.com > Yeni proje > bu kodu yapıştır > createDockHubForm çalıştır > izin ver.
 * Çalışınca Günlük'te (Ctrl+Enter) formun düzenleme ve doldurma linkleri çıkar.
 */
function createDockHubForm() {
  var form = FormApp.create('DockHub 1 Haftalık Kullanım Geri Bildirimi');
  form.setDescription(
    'DockHub, Windows görev çubuğunun yerine geçen bir dock uygulaması. Lütfen bir hafta boyunca günlük işlerinde kullan, ' +
    'sonra bu formu doldur (yaklaşık 8-10 dk). Dürüst ol, kötü yorumlar en çok işe yarayanlardır.\n\n' +
    'Sorun yaşarsan Ayarlar > Hakkında > "Copy diagnostics" ile bilgi kopyalayıp son bölümdeki kutuya yapıştırabilirsin.'
  );
  form.setProgressBar(true);
  form.setCollectEmail(false);

  var scale5 = function (item, low, high) { return item.setBounds(1, 5).setLabels(low, high); };

  // 1) Sen ve bilgisayarın
  form.addPageBreakItem().setTitle('1) Sen ve bilgisayarın');
  form.addMultipleChoiceItem().setTitle('Windows sürümün').setChoiceValues(['Windows 11', 'Windows 10', 'Bilmiyorum']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('Kaç monitör kullanıyorsun?').setChoiceValues(['1', '2', '3 veya daha fazla']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('Bilgisayar türü').setChoiceValues(['Dizüstü', 'Masaüstü']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('Bilgisayarı ne için kullanıyorsun? (en çok)').setChoiceValues(['Ders / ödev', 'Yazılım geliştirme', 'Oyun', 'Ofis / genel', 'Tasarım / medya']).showOtherOption(true).setRequired(true);
  form.addMultipleChoiceItem().setTitle('DockHub\'ı ne kadar süre kullandın?').setChoiceValues(['1-2 gün', '3-5 gün', '6-7 gün', '1 haftadan fazla']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('Hangi modda kullandın?').setChoiceValues(['Görev çubuğunu değiştir (Replace)', 'Görev çubuğuyla birlikte (Both)', 'İkisini de denedim']).setRequired(true);

  // 2) İlk izlenim
  form.addPageBreakItem().setTitle('2) İlk izlenim ve kurulum');
  scale5(form.addScaleItem().setTitle('Kurulum ne kadar kolaydı?').setRequired(true), 'Çok zor', 'Çok kolay');
  form.addMultipleChoiceItem().setTitle('İlk açılışta karşılama turunu gördün mü?').setChoiceValues(['Evet, işime yaradı', 'Evet, geçtim', 'Görmedim']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('Uygulama açılınca Windows görev çubuğuna ne olduğunu anladın mı?').setChoiceValues(['Evet, hemen', 'Biraz zaman aldı', 'Hayır, şaşırdım']).setRequired(true);
  form.addParagraphTextItem().setTitle('İlk 5 dakikada seni şaşırtan, bozan ya da takıldığın bir şey oldu mu?');

  // 3) Günlük kullanım (ızgara)
  form.addPageBreakItem().setTitle('3) Özellikler');
  form.addGridItem()
    .setTitle('Kullandıklarını puanla (1 = çok kötü, 5 = çok iyi). Kullanmadıysan boş bırak.')
    .setRows([
      'Uygulama sabitleme / sürükleyip bırakma',
      'Çalışan uygulamaları görme ve geçiş',
      'Pencere önizlemesi (üzerine gelince)',
      'Win + 1…9 kısayolları',
      'Sistem tepsisi (tray) ikonları',
      'Ses / ağ / pil ikonları',
      'Widget\'ları ekleme ve düzenleme',
      'Ayarlar penceresi ve arama',
      'Görünüm (tema, boyut, kenar, şeffaflık)',
      'Otomatik gizleme',
      'Geri alma (Undo) ve yedekler',
      'Profiller'
    ])
    .setColumns(['1', '2', '3', '4', '5']);

  form.addCheckboxItem().setTitle('Hangi widget\'ları kullandın?').setChoiceValues([
    'Saat', 'Dünya saati', 'Kronometre / Odak / Geri sayım', 'Hatırlatıcılar', 'Yapışkan notlar', 'Takvim', 'Pano geçmişi', 'Klasör yığını', 'Döviz kurları',
    'Şu an çalan', 'Sistem (CPU/RAM)', 'Ağ', 'Hava durumu', 'Ses cihazı', 'Pil', 'Geri dönüşüm kutusu', 'AI kullanım'
  ]).showOtherOption(true);
  form.addParagraphTextItem().setTitle('En çok işine yarayan widget hangisi ve neden?');
  form.addParagraphTextItem().setTitle('Hiç işine yaramayan ya da gereksiz bulduğun widget var mı?');

  // 4) Güvenilirlik
  form.addPageBreakItem().setTitle('4) Sorunlar ve güvenilirlik');
  form.addCheckboxItem().setTitle('Bu hafta hangileriyle karşılaştın?').setChoiceValues([
    'Bir uygulamanın ikonu kayboldu / boş göründü',
    'Açtığım bir uygulama dock\'ta görünmedi',
    'Pencere önizlemesi açılmadı ya da yanlış yerde açıldı',
    'Dock donmuş gibi takıldı',
    'Uygulama çöktü ya da kendiliğinden kapandı',
    'Windows görev çubuğu geri gelmedi / üst üste bindi',
    'Tam ekran uygulamada (oyun, video) dock rahatsız etti',
    'Yüksek CPU / pil tüketimi fark ettim',
    'Hiçbiri'
  ]).setRequired(true);
  form.addParagraphTextItem().setTitle('Bir sorunla karşılaştıysan ne yapıyordun, ne oldu? (tekrarlanabilir adımlar çok işe yarar)');
  scale5(form.addScaleItem().setTitle('DockHub\'a ne kadar güvendin? (Görev çubuğunu bırakmaya cesaret eder miydin?)').setRequired(true), 'Hiç', 'Tamamen');
  form.addMultipleChoiceItem().setTitle('Bilgisayarın hızında fark ettin mi?').setChoiceValues(['Belirgin şekilde yavaşlattı', 'Hafif yavaşlattı', 'Fark etmedim', 'Daha hızlı hissettirdi']).setRequired(true);

  // 5) SUS
  form.addPageBreakItem().setTitle('5) Kullanılabilirlik (SUS)').setHelpText('Her ifadeye ne kadar katıldığını işaretle: 1 = Hiç katılmıyorum, 5 = Tamamen katılıyorum.');
  var sus = [
    'DockHub\'ı sık sık kullanmak isterim.',
    'DockHub\'ı gereksiz yere karmaşık buldum.',
    'DockHub\'ı kullanması kolay buldum.',
    'DockHub\'ı kullanmak için birinin yardımına ihtiyaç duyarım.',
    'DockHub\'daki özelliklerin iyi bir bütün oluşturduğunu düşündüm.',
    'DockHub\'da tutarsızlık çoktu.',
    'Çoğu insanın DockHub\'ı çok hızlı öğreneceğini düşünüyorum.',
    'DockHub\'ı kullanması çok hantal / zahmetliydi.',
    'DockHub\'ı kullanırken kendimi çok güvende hissettim.',
    'DockHub\'ı kullanmaya başlamadan önce çok şey öğrenmem gerekti.'
  ];
  sus.forEach(function (q) { scale5(form.addScaleItem().setTitle(q).setRequired(true), 'Hiç katılmıyorum', 'Tamamen katılıyorum'); });

  // 6) Genel
  form.addPageBreakItem().setTitle('6) Genel değerlendirme');
  form.addScaleItem().setTitle('DockHub\'ı bir arkadaşına önerme ihtimalin?').setBounds(0, 10).setLabels('Asla', 'Kesinlikle').setRequired(true);
  form.addMultipleChoiceItem().setTitle('Hafta sonunda DockHub\'ı kullanmaya devam eder misin?').setChoiceValues(['Evet, kalıcı olarak', 'Belki', 'Hayır, eski görev çubuğuma döneceğim']).setRequired(true);
  form.addParagraphTextItem().setTitle('En çok sevdiğin 3 şey').setRequired(true);
  form.addParagraphTextItem().setTitle('En çok sinir bozan / rahatsız eden 3 şey').setRequired(true);
  form.addParagraphTextItem().setTitle('Eksik olduğunu düşündüğün özellik ya da widget (sihirli değnek olsaydı ne eklerdin?)');
  form.addParagraphTextItem().setTitle('Tasarım hakkında (renkler, boyut, animasyonlar, okunabilirlik, Windows 11 ile uyum) düşüncelerin');
  form.addParagraphTextItem().setTitle('Eklemek istediğin başka bir şey');

  // 7) Teknik bilgi
  form.addPageBreakItem().setTitle('7) İsteğe bağlı: teknik bilgi').setHelpText('Sorun yaşadıysan Ayarlar > Hakkında > Copy diagnostics ile kopyalayıp yapıştır.');
  form.addParagraphTextItem().setTitle('Diagnostics çıktısı (isteğe bağlı)');
  form.addTextItem().setTitle('Adın ya da takma adın (isteğe bağlı, sorunu sormam gerekirse)');

  form.setConfirmationMessage('Teşekkürler! Geri bildirimin çok değerli.');

  // Yanıtları otomatik bir Google E-Tablo'ya topla.
  var ss = SpreadsheetApp.create('DockHub UX Yanıtları');
  form.setDestination(FormApp.DestinationType.SPREADSHEET, ss.getId());

  Logger.log('DÜZENLEME linki: ' + form.getEditUrl());
  Logger.log('ARKADAŞLARA GÖNDERİLECEK link: ' + form.getPublishedUrl());
  Logger.log('Yanıt tablosu: ' + ss.getUrl());
}
