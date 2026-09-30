/**
 * Creates the English one-week DockHub UX feedback form (for international subreddits).
 * Usage: script.google.com > New project > paste this code > run createDockHubFormEn > allow access.
 * The edit link, the public link and the response sheet are printed in the Execution log (Ctrl+Enter).
 * Mirrors create-form.gs, so the Turkish and English answers can be compared question by question.
 */
function createDockHubFormEn() {
  var form = FormApp.create('DockHub: One-Week Feedback');
  form.setDescription(
    'DockHub is an open-source dock that replaces the Windows taskbar. Please use it for your everyday work for about a week, ' +
    'then fill in this form (8-10 minutes). Be blunt: bugs and criticism help the most.\n\n' +
    'If something went wrong, open Settings > About > "Copy diagnostics" and paste the result in the last section.\n' +
    'No sign-in and no email needed. Answers are only used to improve DockHub.'
  );
  form.setProgressBar(true);
  form.setCollectEmail(false);

  var scale5 = function (item, low, high) { return item.setBounds(1, 5).setLabels(low, high); };

  // 1) About you
  form.addPageBreakItem().setTitle('1) You and your PC');
  form.addMultipleChoiceItem().setTitle('Where did you find DockHub?').setChoiceValues([
    'r/Windows11', 'r/windows', 'r/opensource', 'r/software', 'r/desktops', 'r/WindowsHelp', 'r/SideProject', 'GitHub'
  ]).showOtherOption(true).setRequired(true);
  form.addMultipleChoiceItem().setTitle('Region').setChoiceValues(['Europe', 'North America', 'South America', 'Asia', 'Middle East / Africa', 'Oceania']).showOtherOption(true);
  form.addMultipleChoiceItem().setTitle('Windows version').setChoiceValues(['Windows 11', 'Windows 10', 'Not sure']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('How many monitors do you use?').setChoiceValues(['1', '2', '3 or more']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('Computer type').setChoiceValues(['Laptop', 'Desktop']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('Display scaling').setChoiceValues(['100%', '125%', '150%', '175% or more', 'Different per monitor', 'Not sure']);
  form.addMultipleChoiceItem().setTitle('What do you mostly use your PC for?').setChoiceValues(['Study', 'Software development', 'Gaming', 'Office / general', 'Design / media']).showOtherOption(true).setRequired(true);
  form.addCheckboxItem().setTitle('Have you used any of these before?').setChoiceValues([
    'StartAllBack', 'ExplorerPatcher', 'Start11 / Stardock', 'RetroBar', 'Nexus / Winstep', 'RocketDock', 'ObjectDock', 'TranslucentTB', 'A Mac (macOS Dock)', 'None of them'
  ]).showOtherOption(true);
  form.addMultipleChoiceItem().setTitle('How long did you use DockHub?').setChoiceValues(['1-2 days', '3-5 days', '6-7 days', 'More than a week']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('Which mode did you use?').setChoiceValues(['Replace the taskbar', 'Next to the taskbar (Both)', 'Tried both']).setRequired(true);

  // 2) First impression
  form.addPageBreakItem().setTitle('2) First impression and setup');
  scale5(form.addScaleItem().setTitle('How easy was the installation?').setRequired(true), 'Very hard', 'Very easy');
  form.addMultipleChoiceItem().setTitle('Did you see the welcome tour on first launch?').setChoiceValues(['Yes, it helped', 'Yes, I skipped it', 'I did not see it']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('When DockHub started, was it clear what happened to the Windows taskbar?').setChoiceValues(['Yes, right away', 'It took a moment', 'No, it confused me']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('Did Windows SmartScreen or your antivirus warn you about the installer?').setChoiceValues(['No', 'Yes, but I installed anyway', 'Yes, and it almost stopped me']).setRequired(true);
  form.addParagraphTextItem().setTitle('Did anything surprise you, break, or get in your way in the first 5 minutes?');

  // 3) Features
  form.addPageBreakItem().setTitle('3) Features');
  form.addGridItem()
    .setTitle('Rate what you used (1 = very bad, 5 = very good). Leave a row empty if you did not use it.')
    .setRows([
      'Pinning apps / drag and drop',
      'Seeing and switching running apps',
      'Window previews (on hover)',
      'Win + 1…9 shortcuts',
      'Quick launcher (Win + Alt + Space)',
      'System tray icons',
      'Volume / network / battery icons',
      'Adding and arranging widgets',
      'Settings window and search',
      'Appearance (theme, size, edge, transparency)',
      'Auto-hide',
      'Undo and backups',
      'Profiles'
    ])
    .setColumns(['1', '2', '3', '4', '5']);

  form.addCheckboxItem().setTitle('Which widgets did you use?').setChoiceValues([
    'Clock', 'World clock', 'Stopwatch / Focus timer / Countdown / Alarm', 'Reminders', 'To do', 'Sticky notes', 'Calendar',
    'Clipboard history', 'Folder stack', 'Exchange rates', 'Now playing', 'Audio device', 'Brightness', 'Wi-Fi and Bluetooth',
    'System (CPU / RAM)', 'GPU', 'Network', 'Weather', 'Device batteries', 'Recycle bin', 'Screenshot', 'AI usage', 'A web widget'
  ]).showOtherOption(true);
  form.addParagraphTextItem().setTitle('Which widget was the most useful, and why?');
  form.addParagraphTextItem().setTitle('Is there a widget you found useless or unnecessary?');

  // 4) Reliability
  form.addPageBreakItem().setTitle('4) Problems and reliability');
  form.addCheckboxItem().setTitle('Which of these happened to you this week?').setChoiceValues([
    'An app icon disappeared or looked empty',
    'An app I opened did not show up on the dock',
    'A window preview did not open or opened in the wrong place',
    'The dock froze or stuttered',
    'DockHub crashed or closed by itself',
    'The Windows taskbar did not come back / overlapped the dock',
    'The dock got in the way of a full-screen app (game, video)',
    'Maximized windows went under the dock',
    'I noticed high CPU or battery use',
    'Something looked wrong in my language or region format',
    'None of these'
  ]).setRequired(true);
  form.addParagraphTextItem().setTitle('If you hit a problem: what were you doing, and what happened? (Steps to reproduce help a lot.)');
  scale5(form.addScaleItem().setTitle('How much did you trust DockHub? (Would you dare to give up the taskbar?)').setRequired(true), 'Not at all', 'Completely');
  form.addMultipleChoiceItem().setTitle('Did you notice any difference in your PC\'s speed?').setChoiceValues(['Clearly slower', 'Slightly slower', 'No difference', 'Felt faster']).setRequired(true);

  // 5) SUS (standard English wording)
  form.addPageBreakItem().setTitle('5) Usability (SUS)').setHelpText('How much do you agree with each statement? 1 = Strongly disagree, 5 = Strongly agree.');
  var sus = [
    'I think that I would like to use DockHub frequently.',
    'I found DockHub unnecessarily complex.',
    'I thought DockHub was easy to use.',
    'I think that I would need the support of a technical person to be able to use DockHub.',
    'I found the various functions in DockHub were well integrated.',
    'I thought there was too much inconsistency in DockHub.',
    'I would imagine that most people would learn to use DockHub very quickly.',
    'I found DockHub very cumbersome to use.',
    'I felt very confident using DockHub.',
    'I needed to learn a lot of things before I could get going with DockHub.'
  ];
  sus.forEach(function (q) { scale5(form.addScaleItem().setTitle(q).setRequired(true), 'Strongly disagree', 'Strongly agree'); });

  // 6) Overall
  form.addPageBreakItem().setTitle('6) Overall');
  form.addScaleItem().setTitle('How likely are you to recommend DockHub to a friend?').setBounds(0, 10).setLabels('Not at all likely', 'Extremely likely').setRequired(true);
  form.addMultipleChoiceItem().setTitle('After this week, will you keep using DockHub?').setChoiceValues(['Yes, for good', 'Maybe', 'No, I\'m going back to my old taskbar']).setRequired(true);
  form.addParagraphTextItem().setTitle('The 3 things you liked most').setRequired(true);
  form.addParagraphTextItem().setTitle('The 3 things that annoyed you most').setRequired(true);
  form.addParagraphTextItem().setTitle('A missing feature or widget (if you had a magic wand, what would you add?)');
  form.addParagraphTextItem().setTitle('Your thoughts on the design (colors, size, animations, readability, fit with Windows 11)');
  form.addParagraphTextItem().setTitle('Anything else you\'d like to add');

  // 7) Technical info
  form.addPageBreakItem().setTitle('7) Optional: technical info').setHelpText('If you had a problem, copy it from Settings > About > Copy diagnostics and paste it here.');
  form.addParagraphTextItem().setTitle('Diagnostics output (optional)');
  form.addTextItem().setTitle('Your name, nickname or Reddit username (optional, in case I need to ask about a bug)');

  form.setConfirmationMessage('Thank you! Your feedback really helps. Bugs and ideas are also welcome at github.com/sametgurtuna/DockHub/issues');

  // Collect responses in a separate Google Sheet so they don't mix with the Turkish ones.
  var ss = SpreadsheetApp.create('DockHub UX Responses (EN)');
  form.setDestination(FormApp.DestinationType.SPREADSHEET, ss.getId());

  Logger.log('EDIT link: ' + form.getEditUrl());
  Logger.log('PUBLIC link to share: ' + form.getPublishedUrl());
  Logger.log('Response sheet: ' + ss.getUrl());
}
