// src/Snapwell.Core/Localization/Strings の ja.json と en.json をそのまま写した文言（生成物。手で直さない）
window.SW_STRINGS = {
"Backup.Error.ChooseFolder": {
"en": "Couldn't choose a sync folder.",
"ja": "同期フォルダーを選べませんでした。"
},
"Backup.Error.FutureDevice": {
"en": "This device state is from a newer version, so sync choices can't be changed.",
"ja": "新しいバージョンの端末状態のため、同期の設定を変更できません。"
},
"Backup.Error.FutureSettings": {
"en": "Can't sync settings from a newer version. Update Snapwell.",
"ja": "新しいバージョンの設定は同期できません。Snapwell を更新してください。"
},
"Backup.Error.LoadDevice": {
"en": "Couldn't read device sync settings. Check permissions.",
"ja": "端末の同期設定を読み取れません。アクセス権を確認してください。"
},
"Backup.Error.SaveChoice": {
"en": "Couldn't save sync choices. Check permissions.",
"ja": "同期の設定を保存できませんでした。アクセス権を確認してください。"
},
"Backup.Folder.InvalidPath": {
"en": "The sync folder path is invalid.",
"ja": "同期フォルダーのパスが正しくありません。"
},
"Backup.Folder.Overlaps": {
"en": "Choose a folder outside the settings folder, its subfolders, and its parents.",
"ja": "設定ファイルと同じフォルダー、その中、または親フォルダーは選べません。"
},
"Backup.NotSet": {
"en": "Not set",
"ja": "未設定"
},
"Backup.NotSynced": {
"en": "Not synced yet",
"ja": "まだ同期していません"
},
"Backup.Sync.Busy": {
"en": "Syncing: waiting for another operation",
"ja": "同期中: ほかの処理の完了を待っています"
},
"Backup.Sync.ConflictLocalWon": {
"en": "Conflict: used this PC's settings",
"ja": "競合: この PC の設定を採用"
},
"Backup.Sync.ConflictRemoteWon": {
"en": "Conflict: used sync folder settings",
"ja": "競合: 同期先の設定を採用"
},
"Backup.Sync.Failed": {
"en": "Failed: {message}",
"ja": "失敗: {message}"
},
"Backup.Sync.NoChange": {
"en": "No changes",
"ja": "変更なし"
},
"Backup.Sync.Pulled": {
"en": "Imported",
"ja": "取り込み済み"
},
"Backup.Sync.Pushed": {
"en": "Exported",
"ja": "書き出し済み"
},
"Backup.Sync.Rejected": {
"en": "Failed: {message}",
"ja": "失敗: {message}"
},
"Backup.Sync.Skipped": {
"en": "Skipped: {message}",
"ja": "見送り: {message}"
},
"Backup.SyncReason.FileAccess": {
"en": "Couldn't read or write files. Check the connection and permissions.",
"ja": "ファイルを読み書きできません。接続とアクセス権を確認してください。"
},
"Backup.SyncReason.Future": {
"en": "Can't overwrite settings from a newer version. Update Snapwell.",
"ja": "新しいバージョンの設定は上書きできません。Snapwell を更新してください。"
},
"Backup.SyncReason.LocalBroken": {
"en": "Sync was skipped because this PC's settings are damaged.",
"ja": "この PC の設定が壊れているため、同期を見送りました。"
},
"Backup.SyncReason.MissingFolder": {
"en": "The sync folder couldn't be found.",
"ja": "同期フォルダーが見つかりません。"
},
"Backup.SyncReason.NotConfigured": {
"en": "Sync is off, no sync folder is set, or device state is read-only.",
"ja": "同期がオフか、同期先が未設定か、端末状態が読み取り専用です。"
},
"Backup.SyncReason.RemoteBroken": {
"en": "The settings in the sync folder are damaged.",
"ja": "同期先の設定が壊れています。"
},
"Backup.SyncWithTimestamp": {
"en": "{timestamp} · {status}",
"ja": "{timestamp} · {status}"
},
"Backup.Synced": {
"en": "Synced",
"ja": "同期済み"
},
"Backup.Transfer.Exported": {
"en": "Settings exported.",
"ja": "設定を書き出しました。"
},
"Backup.Transfer.Failed": {
"en": "Couldn't complete the file operation. Check the file location and permissions.",
"ja": "ファイルの操作を完了できませんでした。ファイルの場所とアクセス権を確認してください。"
},
"Backup.Transfer.Imported": {
"en": "Imported into this page. Use Save at the bottom to apply.",
"ja": "画面に取り込みました。画面下の「保存」で確定してください。"
},
"Backup.Transfer.JsonFile": {
"en": "JSON file",
"ja": "JSON ファイル"
},
"Backup.Transfer.NoDifferences": {
"en": "No differences from current settings.",
"ja": "いまの設定との違いはありません。"
},
"Backup_CloudHint.Text": {
"en": "Snapwell does not send settings to the cloud. Your file sync service, such as OneDrive or Dropbox, copies files in the sync folder to other PCs. Changes arrive only after that service finishes syncing.",
"ja": "Snapwell 自体には、設定をクラウドへ送る機能はありません。OneDrive や Dropbox など、お使いのファイル同期サービスが、同期フォルダーの中のファイルをほかの PC にコピーします。そのサービスの同期が終わるまで、ほかの PC には反映されません。"
},
"Backup_ExportButton.Content": {
"en": "Export",
"ja": "書き出す"
},
"Backup_ExportDescription.Text": {
"en": "Save settings in one JSON file.",
"ja": "設定を 1 つの JSON ファイルに保存します"
},
"Backup_ExportTitle.Text": {
"en": "Export",
"ja": "書き出す"
},
"Backup_FileTitle.Text": {
"en": "File",
"ja": "ファイル"
},
"Backup_FolderTitle.Text": {
"en": "Sync folder",
"ja": "同期フォルダー"
},
"Backup_ImportButton.Content": {
"en": "Import",
"ja": "取り込む"
},
"Backup_ImportDescription.Text": {
"en": "Review differences before importing. Replace all settings or import selected items.",
"ja": "取り込む前に、いまの設定との違いを表示します。全部を置き換えるか、一部だけ取り込むかを選べます"
},
"Backup_ImportTitle.Text": {
"en": "Import",
"ja": "取り込む"
},
"Backup_LastSyncText.Text": {
"en": "Not synced yet",
"ja": "まだ同期していません"
},
"Backup_LastSyncTitle.Text": {
"en": "Last synced",
"ja": "最後に同期した時刻"
},
"Backup_Lead.Text": {
"en": "Share hotkeys, grids, gaps, feel, and rules across PCs. Startup, Windows features, and sync folder choices apply only to this PC.",
"ja": "ホットキー・分割案・間隔・操作感・ルールを、ほかの PC と共有できます。自動起動・標準機能・同期フォルダーの設定は、この PC だけのものです。"
},
"Backup_SaveHint.Text": {
"en": "After importing, use Save at the bottom to apply.",
"ja": "取り込んだあと、画面下の「保存」で確定します。"
},
"Backup_SyncFolderButton.Content": {
"en": "Change",
"ja": "変更"
},
"Backup_SyncFolderText.Text": {
"en": "Not set",
"ja": "未設定"
},
"Backup_SyncNowButton.Content": {
"en": "Sync now",
"ja": "今すぐ同期"
},
"Backup_SyncToggle.AutomationName": {
"en": "Sync",
"ja": "同期"
},
"Backup_Title.Text": {
"en": "Sync and backup",
"ja": "同期とバックアップ"
},
"Common.Cancel": {
"en": "Cancel",
"ja": "取り消す"
},
"Common.CloseWithoutSaving": {
"en": "Close without saving",
"ja": "保存せず閉じる"
},
"Common.CustomMore": {
"en": "Custom…",
"ja": "カスタム…"
},
"Common.Save": {
"en": "Save",
"ja": "保存"
},
"Common.SaveAndClose": {
"en": "Save and close",
"ja": "保存して閉じる"
},
"Consent.StandardSnap.Body": {
"en": "Turn off Snap windows in Windows Settings while Snapwell is active?\nSnapwell restores your previous settings when you turn it off, exit, or start it after a crash.\n\nChoose Yes to turn off Windows Snap, or No to leave it on.",
"ja": "Windows の設定アプリの「ウィンドウのスナップ」を、Snapwell が有効な間だけ停止します。\nSnapwell を無効にしたとき・終了したとき・異常終了のあとに起動したときに、変更前の設定に戻します。\n\n停止してよいですか？"
},
"Consent.StandardSnap.Title": {
"en": "Snapwell — Change Windows settings",
"ja": "Snapwell — Windows の設定変更"
},
"Corner.DoNotRound": {
"en": "Square corners",
"ja": "角丸なし"
},
"Corner.Inherit": {
"en": "Use global setting",
"ja": "全体設定に従う"
},
"Corner.Round": {
"en": "Force rounded corners",
"ja": "角丸を強制する"
},
"Corner.RoundSmall": {
"en": "Small rounded corners",
"ja": "小さい角丸"
},
"Corner.SystemDefault": {
"en": "Windows default",
"ja": "Windows の既定"
},
"Dialogs.Picker.Uwp": {
"en": "UWP apps share a process name. Use the window class or title.",
"ja": "UWP アプリは、プロセス名が共通です。クラスかタイトルで指定してください。"
},
"Dialogs.Picker.WindowName": {
"en": "{title}, {process}, {class}",
"ja": "{title}、{process}、{class}"
},
"Dialogs.Rule.Add": {
"en": "Add",
"ja": "追加"
},
"Dialogs.Rule.AddTitle": {
"en": "Add rule",
"ja": "ルールを追加"
},
"Dialogs.Rule.CornerUnchanged": {
"en": "{name} (Snapwell leaves it unchanged)",
"ja": "{name}（Snapwell は変更しない）"
},
"Dialogs.Rule.EditTitle": {
"en": "Edit rule",
"ja": "ルールを編集"
},
"Dialogs.Rule.Hint.Exclude": {
"en": "Exclude from moving, resizing, and snapping.",
"ja": "動かす・大きさを変える・吸着させる操作の対象にしません"
},
"Dialogs.Rule.Hint.ForceTarget": {
"en": "Treat pop-ups and similar windows as regular windows.",
"ja": "ポップアップなどでも、通常のウィンドウとして扱います"
},
"Dialogs.Rule.Hint.Inherit": {
"en": "This rule does not change placement.",
"ja": "配置については、このルールでは何も決めません"
},
"Dialogs.Rule.Hint.PositionOnly": {
"en": "Move windows without resizing them.",
"ja": "動かしますが、大きさは変えません"
},
"Dialogs.Rule.Save": {
"en": "Save",
"ja": "保存"
},
"Feel.State.Custom": {
"en": "Custom",
"ja": "カスタム"
},
"Feel.State.Preset": {
"en": "Preset: {feel}",
"ja": "プリセット: {feel}"
},
"Feel.StepMetrics": {
"en": "Settles in {settle} s · Overshoot {overshoot}%",
"ja": "落ち着くまで {settle} 秒 · 行き過ぎ {overshoot}%"
},
"Feel_AccessibilityTitle.Text": {
"en": "Accessibility",
"ja": "アクセシビリティ"
},
"Feel_CardBouncy.AutomationName": {
"en": "Bouncy",
"ja": "はずむ"
},
"Feel_CardCrisp.AutomationName": {
"en": "Crisp",
"ja": "きびきび"
},
"Feel_CardSmooth.AutomationName": {
"en": "Smooth",
"ja": "なめらか"
},
"Feel_CardStandard.AutomationName": {
"en": "Standard",
"ja": "標準"
},
"Feel_CurveLegend.Text": {
"en": "Curve: position after moving one zone with a key (time runs left to right; the dashed line is the target).",
"ja": "曲線: キー操作で 1 区画動かしたときの位置の変化（左から右へ時間、破線が目標）"
},
"Feel_CurveThrowNote.Text": {
"en": "The same natural frequency and damping ratio also shape the speed and return of a throw.",
"ja": "投げたときも、同じ固有振動数と減衰比で動きの速さや戻り方が決まります。"
},
"Feel_DampingDescription.Text": {
"en": "Higher values reduce momentum.",
"ja": "大きいほど勢いを抑えます"
},
"Feel_DampingRatioBox.AutomationName": {
"en": "Damping ratio ζ",
"ja": "減衰比 ζ"
},
"Feel_DampingRatioSlider.AutomationName": {
"en": "Damping ratio ζ (slider)",
"ja": "減衰比 ζ（スライダー）"
},
"Feel_DampingTitle.Text": {
"en": "Damping ratio ζ",
"ja": "減衰比 ζ"
},
"Feel_DerivedTitle.Text": {
"en": "Derived values",
"ja": "導かれる値"
},
"Feel_DetailsTitle.Text": {
"en": "Details",
"ja": "詳細"
},
"Feel_FeelResetButton.Content": {
"en": "Reset to preset",
"ja": "プリセットに戻す"
},
"Feel_FrequencyBox.AutomationName": {
"en": "Natural frequency f (Hz)",
"ja": "固有振動数 f (Hz)"
},
"Feel_FrequencyDescription.Text": {
"en": "Controls speed with key actions, attraction from a distance, and slowing after a throw.",
"ja": "キー操作での動きの速さと、投げたあとの遠方からの引き寄せ・減速に効きます"
},
"Feel_FrequencySlider.AutomationName": {
"en": "Natural frequency f (Hz) (slider)",
"ja": "固有振動数 f (Hz)（スライダー）"
},
"Feel_FrequencyTitle.Text": {
"en": "Natural frequency f",
"ja": "固有振動数 f"
},
"Feel_Lead.Text": {
"en": "Adjust how thrown windows move. Natural frequency and damping ratio control speed and return. A preset sets all four values except snap radius. Changing a value makes it custom.",
"ja": "投げたときの動き方を調整します。動きの速さと戻り方は固有振動数と減衰比で決まります。プリセットを選ぶと、吸着半径を除く 4 つの値が変わり、どれかを変えると「カスタム」になります。"
},
"Feel_MagnetDescription.Text": {
"en": "Strength of attraction near the resting point, inside the snap radius. Attraction from a distance stays the same (0–2).",
"ja": "吸着半径の内側で、停止点の近くへ吸い付く強さです。遠方からの引き寄せは変わりません (0〜2)"
},
"Feel_MagnetStrengthBox.AutomationName": {
"en": "Magnet strength",
"ja": "磁石の強さ"
},
"Feel_MagnetStrengthSlider.AutomationName": {
"en": "Magnet strength (slider)",
"ja": "磁石の強さ（スライダー）"
},
"Feel_MagnetTitle.Text": {
"en": "Magnet strength",
"ja": "磁石の強さ"
},
"Feel_MassBox.AutomationName": {
"en": "Mass m",
"ja": "質量 m"
},
"Feel_MassDescription.Text": {
"en": "The reference value for stiffness k and damping c. With the same natural frequency and damping ratio, increasing mass increases the displayed k and c.",
"ja": "固さ k と減衰 c の基準になる値です。同じ固有振動数・減衰比でも、質量を大きくすると k と c の表示値が大きくなります。"
},
"Feel_MassSlider.AutomationName": {
"en": "Mass m (slider)",
"ja": "質量 m（スライダー）"
},
"Feel_MassTitle.Text": {
"en": "Mass m",
"ja": "質量 m"
},
"Feel_ModeBox.AutomationName": {
"en": "Snap target",
"ja": "吸着の方式"
},
"Feel_ModeCorners.Content": {
"en": "Corners — keep size",
"ja": "四隅 — サイズを変えずに移動"
},
"Feel_ModeDescription.Text": {
"en": "Where thrown windows snap.",
"ja": "投げたウィンドウを吸い寄せる対象"
},
"Feel_ModeTitle.Text": {
"en": "Snap target",
"ja": "吸着の方式"
},
"Feel_ModeZones.Content": {
"en": "Cells — halves, 1/4, max",
"ja": "区画 — 半分・四分割・最大化"
},
"Feel_MotionDescription.Text": {
"en": "Move straight to the snap target without animation. Also applies when Windows Animation effects are off.",
"ja": "オンにすると、アニメーションせずに吸着先へすぐ移動します。Windows の「アニメーション効果」がオフのときも同じです"
},
"Feel_MotionSwitch.AutomationName": {
"en": "Reduce motion",
"ja": "動きを減らす"
},
"Feel_MotionTitle.Text": {
"en": "Reduce motion",
"ja": "動きを減らす"
},
"Feel_PresetName_Bouncy.Text": {
"en": "Bouncy",
"ja": "はずむ"
},
"Feel_PresetName_Crisp.Text": {
"en": "Crisp",
"ja": "きびきび"
},
"Feel_PresetName_Smooth.Text": {
"en": "Smooth",
"ja": "なめらか"
},
"Feel_PresetName_Standard.Text": {
"en": "Standard",
"ja": "標準"
},
"Feel_RadiusDescription.Text": {
"en": "Distance of strong attraction (px)",
"ja": "強く吸い込まれる距離 (px)"
},
"Feel_RadiusTitle.Text": {
"en": "Snap radius",
"ja": "吸着半径"
},
"Feel_ResizeTimingBox.AutomationName": {
"en": "Size change timing",
"ja": "サイズの変え方"
},
"Feel_ResizeTimingDescription.Text": {
"en": "Changing size while moving may cause rendering glitches in some windows.",
"ja": "移動しながら変えると、ウィンドウによっては描画が乱れることがあります"
},
"Feel_ResizeTimingOnLand.Content": {
"en": "Change on landing",
"ja": "着地時に変える"
},
"Feel_ResizeTimingTitle.Text": {
"en": "Size change timing",
"ja": "サイズの変え方"
},
"Feel_ResizeTimingWhileMoving.Content": {
"en": "Change while moving",
"ja": "移動しながら変える"
},
"Feel_SnapRadiusBox.AutomationName": {
"en": "Snap radius (px)",
"ja": "吸着半径 (px)"
},
"Feel_SnapRadiusSlider.AutomationName": {
"en": "Snap radius (px) (slider)",
"ja": "吸着半径 (px)（スライダー）"
},
"Feel_Title.Text": {
"en": "Feel",
"ja": "操作感"
},
"Feel_TrialCard.AutomationName": {
"en": "Trial window. Throw by dragging the title bar or using arrow keys.",
"ja": "試すウィンドウ。タイトルバーをドラッグするか、矢印キーを押して投げます"
},
"Feel_TrialCornersButton.Content": {
"en": "Corners",
"ja": "四隅"
},
"Feel_TrialExpander.AutomationName": {
"en": "Try it",
"ja": "試す"
},
"Feel_TrialHint.Text": {
"en": "Throw using the title bar; dragging inside won't move it. The five current values and Snapwell's target rules apply. Below 150 px/s, it won't snap. In Cells mode, throw upward from the center to maximize.",
"ja": "ウィンドウのタイトルバーをつかんで投げます（ウィンドウの中をつかんでも動きません）。いまの 5 つの値で動き、止まる先は Snapwell 本体と同じルールで決まります（離した速さが 150 px/s 未満だと吸着しません。「区画」では、中央から上へ強く投げると最大化になります）"
},
"Feel_TrialThrowButton.Content": {
"en": "Throw randomly",
"ja": "ランダムに投げる"
},
"Feel_TrialTitle.Text": {
"en": "Try it",
"ja": "試す"
},
"Feel_TrialWindowLabel.Text": {
"en": "Window",
"ja": "Window"
},
"Feel_TrialZonesButton.Content": {
"en": "Cells",
"ja": "区画"
},
"General.Device.FutureEnabled": {
"en": "Can't turn Snapwell on or off because the device state is from a newer version. Update Snapwell.",
"ja": "新しいバージョンの端末状態のため、有効・無効を切り替えられません。Snapwell を更新してください。"
},
"General.Device.SaveEnabledFailed": {
"en": "Couldn't save the on/off state. Check the file location and permissions.",
"ja": "有効・無効を保存できませんでした。ファイルの場所とアクセス権を確認してください。"
},
"General.Device.Unreadable": {
"en": "Can't read the device state. Check the file location and permissions, then reopen settings.",
"ja": "端末状態を読み取れません。ファイルの場所とアクセス権を確認して、開き直してください。"
},
"General.Language.Future": {
"en": "Can't change the language because this PC's settings were saved by a newer version. Update Snapwell.",
"ja": "この PC の設定が新しいバージョンで保存されているため、言語を変更できません。Snapwell を更新してください。"
},
"General.Language.SaveFailed": {
"en": "Couldn't save the language. Check the file location and permissions.",
"ja": "言語を保存できませんでした。ファイルの場所とアクセス権を確認してください。"
},
"General.Startup.AdminNextSignIn": {
"en": "Snapwell will start as administrator at your next sign-in.",
"ja": "次のサインインから管理者として起動します。"
},
"General.Startup.Cancelled": {
"en": "No changes were made because administrator access wasn't granted.",
"ja": "管理者権限の許可が得られなかったため、変更していません。"
},
"General.Startup.Elsewhere": {
"en": "Snapwell from another location is registered. Turn this off and on to use the copy in this folder.",
"ja": "別の場所の Snapwell が登録されています。オフにしてからオンにすると、このフォルダーの Snapwell に置き換えます"
},
"General.Startup.ExpandablePath": {
"en": "Can't register administrator startup from a path containing %.",
"ja": "パスに % を含む場所では管理者起動を登録できません"
},
"General.Startup.ExpandablePathError": {
"en": "Can't register administrator startup from a path containing %.",
"ja": "パスに % を含む場所では管理者起動を登録できません。"
},
"General.Startup.Failed": {
"en": "Couldn't read or write auto-start registration. Check permissions.",
"ja": "自動起動の登録を読み書きできませんでした。アクセス権を確認してください。"
},
"General.Startup.MissingExecutable": {
"en": "Can't set up auto-start because Snapwell.exe isn't in the same folder.",
"ja": "Snapwell.exe が同じフォルダーにないため、自動起動を設定できません"
},
"General.Startup.PendingRestore": {
"en": "The previous auto-start settings haven't been restored. Turn administrator startup on and off to retry.",
"ja": "自動起動の設定を元に戻せていません。管理者起動をオンにしてからオフにすると再試行します。"
},
"General.Startup.ProtectedLocation": {
"en": "Place Snapwell in a protected location on a local fixed drive, such as Program Files. Links aren't supported.",
"ja": "ローカル固定ディスクの保護された場所（Program Files など）に置いてください。リンクは使えません"
},
"General.Startup.TaskFailed": {
"en": "Couldn't register with Task Scheduler.",
"ja": "タスク スケジューラに登録できませんでした。"
},
"General.Startup.UnprotectedLocation": {
"en": "Use a location that only administrators can write to.",
"ja": "管理者以外が書き込めない場所に配置してください。"
},
"General_AdminStartDescription.Text": {
"en": "Also move windows of apps running as administrator. Uses Task Scheduler and shows a Windows confirmation. Takes effect at your next sign-in.",
"ja": "管理者権限で動くアプリのウィンドウも動かせます。タスク スケジューラに登録し、Windows の確認画面が表示されます。次のサインインから有効です"
},
"General_AdminStartTitle.Text": {
"en": "Start as administrator",
"ja": "管理者として起動する"
},
"General_AdminStartToggle.AutomationName": {
"en": "Start as administrator",
"ja": "管理者として起動する"
},
"General_AutoStartDescription.Text": {
"en": "Adds Snapwell to Startup.",
"ja": "スタートアップに登録します"
},
"General_AutoStartTitle.Text": {
"en": "Start at sign-in",
"ja": "サインイン時に起動する"
},
"General_AutoStartToggle.AutomationName": {
"en": "Start at sign-in",
"ja": "サインイン時に起動する"
},
"General_EnabledDescription.Text": {
"en": "Turning this off restores Windows Snap. You can also use the tray menu. Changes apply immediately.",
"ja": "オフにすると、標準のスナップ機能を元の状態に戻します。トレイのメニューからも操作できます。保存を待たず、すぐに反映されます"
},
"General_EnabledTitle.Text": {
"en": "Turn on Snapwell",
"ja": "Snapwell を有効にする"
},
"General_EnabledToggle.AutomationName": {
"en": "Turn on Snapwell",
"ja": "Snapwell を有効にする"
},
"General_LanguageAuto.Content": {
"en": "Auto (match Windows)",
"ja": "自動（OS に合わせる）"
},
"General_LanguageChineseSimplified.Content": {
"en": "简体中文",
"ja": "简体中文"
},
"General_LanguageChineseTraditional.Content": {
"en": "繁體中文",
"ja": "繁體中文"
},
"General_LanguageEnglish.Content": {
"en": "English",
"ja": "English"
},
"General_LanguageJapanese.Content": {
"en": "日本語",
"ja": "日本語"
},
"General_LanguageSelect.AutomationName": {
"en": "Language",
"ja": "言語"
},
"General_LanguageTitle.Text": {
"en": "Language",
"ja": "言語"
},
"General_Lead.Text": {
"en": "Basics for startup and behavior.",
"ja": "起動と動作の基本です。"
},
"General_MoveFixedSizeDescription.Text": {
"en": "Align within cells without resizing.",
"ja": "サイズは変えず、区画の中で位置だけを合わせます"
},
"General_MoveFixedSizeTitle.Text": {
"en": "Move fixed-size windows",
"ja": "サイズ固定のウィンドウも動かす"
},
"General_MoveFixedSizeToggle.AutomationName": {
"en": "Move fixed-size windows",
"ja": "サイズ固定のウィンドウも動かす"
},
"General_MovePopupDescription.Text": {
"en": "Move small windows, such as those opened from the taskbar, with hotkeys or dragging without taking focus. They aren't included in arrangement, and some can't be dragged.",
"ja": "タスクバーから開く小さなウィンドウなど。フォーカスを奪わずに、ホットキーとドラッグで動かします（整列の対象にはなりません。ドラッグで動かせないものもあります）"
},
"General_MovePopupTitle.Text": {
"en": "Move pop-ups and flyouts",
"ja": "ポップアップとフライアウトも動かす"
},
"General_MovePopupToggle.AutomationName": {
"en": "Move pop-ups and flyouts",
"ja": "ポップアップとフライアウトも動かす"
},
"General_Title.Text": {
"en": "General",
"ja": "一般"
},
"General_UpdateAutoDescription.Text": {
"en": "When enabled, Snapwell connects to GitHub to fetch only the latest version information. Only a User-Agent containing the version is sent; no identifiers are sent. Off by default.",
"ja": "オンにすると GitHub に接続して最新バージョンの情報だけを取得します。送るのはバージョンを含む User-Agent だけで、識別子は送りません。既定はオフです。"
},
"General_UpdateAutoTitle.Text": {
"en": "Check for updates automatically",
"ja": "更新を自動で確認する"
},
"General_UpdateAutoToggle.AutomationName": {
"en": "Check for updates automatically",
"ja": "更新を自動で確認する"
},
"General_UpdateCheckNowButton.Content": {
"en": "Check now",
"ja": "今すぐ確認"
},
"General_UpdateCheckNowDescription.Text": {
"en": "Press to connect once and check for updates.",
"ja": "ボタンを押すと、1 回だけ通信して更新を確認します。"
},
"General_UpdateCheckNowTitle.Text": {
"en": "Check now",
"ja": "今すぐ確認"
},
"General_UpdateDownload.Content": {
"en": "Download in browser",
"ja": "ブラウザーでダウンロード"
},
"General_UpdateOfferRequired": {
"en": "Your version is below the minimum supported version. Please update.",
"ja": "現在のバージョンは最低サポートバージョンを下回っています。更新してください。"
},
"General_UpdateOfferTitle": {
"en": "Version {version} is available",
"ja": "新しいバージョン {version} があります"
},
"General_UpdateSaveFailed": {
"en": "Couldn’t save the update settings. Check the file location and permissions.",
"ja": "更新の設定を保存できませんでした。ファイルの場所とアクセス権を確認してください。"
},
"General_UpdateSkip.Content": {
"en": "Skip this version",
"ja": "このバージョンをスキップ"
},
"General_UpdateStatusChecking": {
"en": "Checking…",
"ja": "確認中…"
},
"General_UpdateStatusDevelopmentBuild": {
"en": "Development builds don’t check for updates.",
"ja": "開発用ビルドでは更新を確認しません。"
},
"General_UpdateStatusFailed": {
"en": "Couldn’t check for updates. Try again later.",
"ja": "更新を確認できませんでした。後でもう一度お試しください。"
},
"General_UpdateStatusLastChecked": {
"en": "Last checked: {time}",
"ja": "最終確認: {time}"
},
"General_UpdateStatusNoPublicKey": {
"en": "No public key is configured, so updates can’t be checked.",
"ja": "公開鍵が設定されていないため、更新を確認できません。"
},
"General_UpdateStatusSkipped": {
"en": "This version has been skipped.",
"ja": "このバージョンはスキップ済みです。"
},
"General_UpdateStatusUpToDate": {
"en": "You’re up to date.",
"ja": "最新のバージョンです。"
},
"General_UpdatesTitle.Text": {
"en": "Updates",
"ja": "更新"
},
"General_WindowsTitle.Text": {
"en": "Included windows",
"ja": "対象のウィンドウ"
},
"Grid.CustomDimensions": {
"en": "Custom ({columns} × {rows})",
"ja": "カスタム ({columns} × {rows})"
},
"Grid.Monitor.Label": {
"en": "{number} · {width} × {height} · {scale}%",
"ja": "{number} · {width} × {height} · {scale}%"
},
"Grid.Monitor.Primary": {
"en": "{label} (main)",
"ja": "{label}（メイン）"
},
"Grid.Monitors.Shared": {
"en": "Shared by all monitors",
"ja": "全モニター共通"
},
"Grid.Monitors.SharedHint": {
"en": "Monitors without a custom grid use the shared grid.",
"ja": "未設定のモニターは共通の案を使います"
},
"Grid.Monitors.Unavailable": {
"en": "Can't get the monitor list. Open the list to retry.",
"ja": "モニター一覧を取得できません。一覧を開くと再試行します"
},
"Grid.Preview.Cells.One": {
"en": "{columns} × {rows} cell",
"ja": "{columns} × {rows} 区画"
},
"Grid.Preview.Cells.Other": {
"en": "{columns} × {rows} cells",
"ja": "{columns} × {rows} 区画"
},
"Grid_AnchorCenter.Content": {
"en": "Center",
"ja": "中心"
},
"Grid_AnchorDescription.Text": {
"en": "The point that stays fixed while resizing.",
"ja": "拡大・縮小するときに動かさない位置"
},
"Grid_AnchorNearEdge.Content": {
"en": "Nearest edge",
"ja": "近い辺"
},
"Grid_AnchorTitle.Text": {
"en": "Anchor",
"ja": "固定する位置"
},
"Grid_AnchorTopLeft.Content": {
"en": "Top left",
"ja": "左上"
},
"Grid_ChipCustom.Content": {
"en": "Custom…",
"ja": "カスタム…"
},
"Grid_ColumnsBox.AutomationName": {
"en": "Columns",
"ja": "列数"
},
"Grid_ColumnsDescription.Text": {
"en": "Horizontal cells (1–64)",
"ja": "横方向の区画数 (1〜64)"
},
"Grid_ColumnsTitle.Text": {
"en": "Columns",
"ja": "列数"
},
"Grid_CustomPlan.Content": {
"en": "Custom",
"ja": "カスタム"
},
"Grid_EdgeBox.AutomationName": {
"en": "Edge margin (px)",
"ja": "画面端の余白 (px)"
},
"Grid_EdgePixelsDescription.Text": {
"en": "Margin around the monitor work area (0–200 px)",
"ja": "モニターの作業領域の外周の余白 (0〜200 px)"
},
"Grid_EdgePixelsTitle.Text": {
"en": "Edge margin (px)",
"ja": "画面端の余白 (px)"
},
"Grid_EdgeSlider.AutomationName": {
"en": "Edge margin",
"ja": "画面端の余白"
},
"Grid_EdgeTitle.Text": {
"en": "Edge margin",
"ja": "画面端の余白"
},
"Grid_GapBox.AutomationName": {
"en": "Window gap (px)",
"ja": "ウィンドウの間隔 (px)"
},
"Grid_GapPixelsDescription.Text": {
"en": "Space between adjacent cells (0–200 px)",
"ja": "隣り合う区画の間の隙間 (0〜200 px)"
},
"Grid_GapPixelsTitle.Text": {
"en": "Window gap (px)",
"ja": "ウィンドウの間隔 (px)"
},
"Grid_GapSlider.AutomationName": {
"en": "Window gap",
"ja": "ウィンドウの間隔"
},
"Grid_GapTitle.Text": {
"en": "Window gap",
"ja": "ウィンドウの間隔"
},
"Grid_Lead.Text": {
"en": "Choose a grid for each monitor. Window sizes are whole multiples of a cell.",
"ja": "モニターごとに分割案を決めます。1 区画が基本単位で、ウィンドウのサイズはその整数倍になります。"
},
"Grid_LinkEdgeCheck.Content": {
"en": "Match gap",
"ja": "間隔と同じにする"
},
"Grid_MonitorBox.AutomationName": {
"en": "Monitor",
"ja": "モニター"
},
"Grid_MonitorStatus.Text": {
"en": "Monitors without a custom grid use the shared grid.",
"ja": "未設定のモニターは共通の案を使います"
},
"Grid_MonitorTitle.Text": {
"en": "Monitor",
"ja": "モニター"
},
"Grid_MouseResizeTitle.Text": {
"en": "Resize with the mouse",
"ja": "マウスでのリサイズ"
},
"Grid_NumbersTitle.Text": {
"en": "Exact values",
"ja": "数値での指定"
},
"Grid_PlanBox.Header": {
"en": "Grid",
"ja": "分割案"
},
"Grid_PlanTitle.Text": {
"en": "Grid",
"ja": "分割案"
},
"Grid_ResizeAnchorBox.AutomationName": {
"en": "Anchor",
"ja": "固定する位置"
},
"Grid_ResizeHint.Text": {
"en": "Use Ctrl+Alt+Shift+arrow keys to grow or shrink the active window by cells. Change the keys on the Hotkeys page.",
"ja": "Ctrl+Alt+Shift+矢印キーで、前面のウィンドウを区画単位で広げたり縮めたりします（キーはホットキーのページで変えられます）。"
},
"Grid_ResizeStepBox.AutomationName": {
"en": "Resize step",
"ja": "1 回の拡大・縮小の幅"
},
"Grid_ResizeStepTitle.Text": {
"en": "Resize step",
"ja": "1 回の拡大・縮小の幅"
},
"Grid_ResizeTitle.Text": {
"en": "Resize with hotkeys",
"ja": "ホットキーでの拡大・縮小"
},
"Grid_RowsBox.AutomationName": {
"en": "Rows",
"ja": "行数"
},
"Grid_RowsDescription.Text": {
"en": "Vertical cells (1–64)",
"ja": "縦方向の区画数 (1〜64)"
},
"Grid_RowsTitle.Text": {
"en": "Rows",
"ja": "行数"
},
"Grid_SnapAfterResizeDescription.Text": {
"en": "Release a window edge to align it to the nearest cell boundary. The target is shown in blue as you drag.",
"ja": "ウィンドウの縁をドラッグして離すと、いちばん近い区画の境界にぴたりとそろえます。ドラッグ中は、そろう先を青く表示します"
},
"Grid_SnapAfterResizeTitle.Text": {
"en": "Snap after resizing",
"ja": "手動でリサイズしたら区画にそろえる"
},
"Grid_SnapAfterResizeToggle.AutomationName": {
"en": "Snap after resizing",
"ja": "手動でリサイズしたら区画にそろえる"
},
"Grid_SpacingHint.Text": {
"en": "Cells use the visible window edges. With a gap of 0, adjacent windows touch.",
"ja": "区画はウィンドウの見た目の輪郭を基準に決めるため、間隔を 0 にすると、並べたウィンドウどうしがぴったり接します。"
},
"Grid_Step1.Content": {
"en": "1 cell",
"ja": "1 区画"
},
"Grid_Step2.Content": {
"en": "2 cells",
"ja": "2 区画"
},
"Grid_Step3.Content": {
"en": "3 cells",
"ja": "3 区画"
},
"Grid_Step4.Content": {
"en": "4 cells",
"ja": "4 区画"
},
"Grid_Title.Text": {
"en": "Grid and gaps",
"ja": "分割と間隔"
},
"Hotkey.Command.ArrangeAllMonitors": {
"en": "Arrange all monitors (grid)",
"ja": "全モニターを整列（グリッド）"
},
"Hotkey.Command.ArrangeColumns": {
"en": "Arrange this monitor side by side",
"ja": "現在のモニターを横一列に整列"
},
"Hotkey.Command.ArrangeGrid": {
"en": "Arrange this monitor (grid)",
"ja": "現在のモニターを整列（グリッド）"
},
"Hotkey.Command.ArrangeMainAndStack": {
"en": "Arrange this monitor: main and stack",
"ja": "現在のモニターをメインとスタックで整列"
},
"Hotkey.Command.ArrangeRows": {
"en": "Arrange this monitor stacked",
"ja": "現在のモニターを縦一列に整列"
},
"Hotkey.Command.BottomHalf": {
"en": "Bottom half",
"ja": "下半分"
},
"Hotkey.Command.BottomLeftQuarter": {
"en": "Bottom-left quarter",
"ja": "左下の四分割"
},
"Hotkey.Command.BottomRightQuarter": {
"en": "Bottom-right quarter",
"ja": "右下の四分割"
},
"Hotkey.Command.CenterWindow": {
"en": "Center",
"ja": "中央に配置"
},
"Hotkey.Command.CenterWindowKeepSize": {
"en": "Center (keep size)",
"ja": "中央に配置（サイズ維持）"
},
"Hotkey.Command.GrowDown.Center.One": {
"en": "Grow height both ways by {step} cell",
"ja": "高さを上下へ {step} 区画広げる"
},
"Hotkey.Command.GrowDown.Center.Other": {
"en": "Grow height both ways by {step} cells",
"ja": "高さを上下へ {step} 区画広げる"
},
"Hotkey.Command.GrowDown.NearEdge.One": {
"en": "Grow height by {step} cell",
"ja": "高さを {step} 区画広げる"
},
"Hotkey.Command.GrowDown.NearEdge.Other": {
"en": "Grow height by {step} cells",
"ja": "高さを {step} 区画広げる"
},
"Hotkey.Command.GrowDown.One": {
"en": "Grow down by {step} cell",
"ja": "下へ {step} 区画広げる"
},
"Hotkey.Command.GrowDown.Other": {
"en": "Grow down by {step} cells",
"ja": "下へ {step} 区画広げる"
},
"Hotkey.Command.GrowRight.Center.One": {
"en": "Grow width both ways by {step} cell",
"ja": "幅を左右へ {step} 区画広げる"
},
"Hotkey.Command.GrowRight.Center.Other": {
"en": "Grow width both ways by {step} cells",
"ja": "幅を左右へ {step} 区画広げる"
},
"Hotkey.Command.GrowRight.NearEdge.One": {
"en": "Grow width by {step} cell",
"ja": "幅を {step} 区画広げる"
},
"Hotkey.Command.GrowRight.NearEdge.Other": {
"en": "Grow width by {step} cells",
"ja": "幅を {step} 区画広げる"
},
"Hotkey.Command.GrowRight.One": {
"en": "Grow right by {step} cell",
"ja": "右へ {step} 区画広げる"
},
"Hotkey.Command.GrowRight.Other": {
"en": "Grow right by {step} cells",
"ja": "右へ {step} 区画広げる"
},
"Hotkey.Command.LeftHalf": {
"en": "Left half",
"ja": "左半分"
},
"Hotkey.Command.Maximize": {
"en": "Maximize",
"ja": "最大化"
},
"Hotkey.Command.MoveCellDown": {
"en": "Move down 1 cell",
"ja": "下へ 1 区画移動"
},
"Hotkey.Command.MoveCellLeft": {
"en": "Move left 1 cell",
"ja": "左へ 1 区画移動"
},
"Hotkey.Command.MoveCellRight": {
"en": "Move right 1 cell",
"ja": "右へ 1 区画移動"
},
"Hotkey.Command.MoveCellUp": {
"en": "Move up 1 cell",
"ja": "上へ 1 区画移動"
},
"Hotkey.Command.NextMonitor": {
"en": "Next monitor",
"ja": "次のモニターへ"
},
"Hotkey.Command.OpenLauncher": {
"en": "Open launcher",
"ja": "ランチャーを開く"
},
"Hotkey.Command.PreviousMonitor": {
"en": "Previous monitor",
"ja": "前のモニターへ"
},
"Hotkey.Command.RightHalf": {
"en": "Right half",
"ja": "右半分"
},
"Hotkey.Command.ShrinkLeft.Center.One": {
"en": "Shrink width both ways by {step} cell",
"ja": "幅を左右から {step} 区画縮める"
},
"Hotkey.Command.ShrinkLeft.Center.Other": {
"en": "Shrink width both ways by {step} cells",
"ja": "幅を左右から {step} 区画縮める"
},
"Hotkey.Command.ShrinkLeft.NearEdge.One": {
"en": "Shrink width by {step} cell",
"ja": "幅を {step} 区画縮める"
},
"Hotkey.Command.ShrinkLeft.NearEdge.Other": {
"en": "Shrink width by {step} cells",
"ja": "幅を {step} 区画縮める"
},
"Hotkey.Command.ShrinkLeft.One": {
"en": "Shrink left by {step} cell",
"ja": "左へ {step} 区画縮める"
},
"Hotkey.Command.ShrinkLeft.Other": {
"en": "Shrink left by {step} cells",
"ja": "左へ {step} 区画縮める"
},
"Hotkey.Command.ShrinkUp.Center.One": {
"en": "Shrink height both ways by {step} cell",
"ja": "高さを上下から {step} 区画縮める"
},
"Hotkey.Command.ShrinkUp.Center.Other": {
"en": "Shrink height both ways by {step} cells",
"ja": "高さを上下から {step} 区画縮める"
},
"Hotkey.Command.ShrinkUp.NearEdge.One": {
"en": "Shrink height by {step} cell",
"ja": "高さを {step} 区画縮める"
},
"Hotkey.Command.ShrinkUp.NearEdge.Other": {
"en": "Shrink height by {step} cells",
"ja": "高さを {step} 区画縮める"
},
"Hotkey.Command.ShrinkUp.One": {
"en": "Shrink up by {step} cell",
"ja": "上へ {step} 区画縮める"
},
"Hotkey.Command.ShrinkUp.Other": {
"en": "Shrink up by {step} cells",
"ja": "上へ {step} 区画縮める"
},
"Hotkey.Command.SnapAdjacentDown": {
"en": "Snap to lower neighbor",
"ja": "下の隣へ寄せる"
},
"Hotkey.Command.SnapAdjacentLeft": {
"en": "Snap to left neighbor",
"ja": "左の隣へ寄せる"
},
"Hotkey.Command.SnapAdjacentRight": {
"en": "Snap to right neighbor",
"ja": "右の隣へ寄せる"
},
"Hotkey.Command.SnapAdjacentUp": {
"en": "Snap to upper neighbor",
"ja": "上の隣へ寄せる"
},
"Hotkey.Command.ToggleEnabled": {
"en": "Pause or resume",
"ja": "一時停止・再開"
},
"Hotkey.Command.TopHalf": {
"en": "Top half",
"ja": "上半分"
},
"Hotkey.Command.TopLeftQuarter": {
"en": "Top-left quarter",
"ja": "左上の四分割"
},
"Hotkey.Command.TopRightQuarter": {
"en": "Top-right quarter",
"ja": "右上の四分割"
},
"Hotkey.Group.Adjacent": {
"en": "Snap to neighbor",
"ja": "隣へ寄せる"
},
"Hotkey.Group.Arrange": {
"en": "Arrange",
"ja": "整列"
},
"Hotkey.Group.Misc": {
"en": "Other",
"ja": "そのほか"
},
"Hotkey.Group.Monitor": {
"en": "Monitors",
"ja": "モニター"
},
"Hotkey.Group.Move": {
"en": "Move by cells",
"ja": "区画の移動"
},
"Hotkey.Group.Placement": {
"en": "Placement",
"ja": "配置"
},
"Hotkey.Group.Resize": {
"en": "Grow and shrink",
"ja": "広げる・縮める"
},
"Hotkeys.Anchor.CenterDescription": {
"en": "The center stays fixed. Both sides move",
"ja": "中心を固定。左右・上下の両側が動きます"
},
"Hotkeys.Anchor.CenterName": {
"en": "Center",
"ja": "中心"
},
"Hotkeys.Anchor.NearEdgeDescription": {
"en": "The edge nearest the work area boundary stays fixed. The far edge moves",
"ja": "作業領域の端に近い辺を固定。遠い側の辺が動きます"
},
"Hotkeys.Anchor.NearEdgeName": {
"en": "Nearest edge",
"ja": "近い辺"
},
"Hotkeys.Anchor.Note": {
"en": "Anchor: {name}. {description}. Change it in Grid and gaps.",
"ja": "固定する位置: {name}（{description}）。「分割と間隔」の「固定する位置」で変えられます"
},
"Hotkeys.Anchor.TopLeftDescription": {
"en": "The top left stays fixed. The right and bottom edges move",
"ja": "左上を固定。右辺と下辺が動きます"
},
"Hotkeys.Anchor.TopLeftName": {
"en": "Top left",
"ja": "左上"
},
"Hotkeys.Capture.Announcement": {
"en": "{name}: {text}",
"ja": "{name}: {text}"
},
"Hotkeys.Capture.Busy": {
"en": "Another settings window is capturing a key",
"ja": "ほかの設定画面でキーを取り込み中です"
},
"Hotkeys.Capture.Cancel": {
"en": "Cancel",
"ja": "やめる"
},
"Hotkeys.Capture.DuplicateMessage": {
"en": "{chord} is also assigned to “{names}”.",
"ja": "「{names}」と同じキー {chord} です。"
},
"Hotkeys.Capture.InUseMessage": {
"en": "{chord} is used by another app or Windows. Assigning it may stop it working there. Windows-reserved keys may not reach Snapwell.",
"ja": "{chord} は、ほかのアプリか Windows が使っているキーです。割り当てると、そのアプリでは使えなくなります（Windows が予約しているキーは、割り当てても Snapwell に届かないことがあります）。"
},
"Hotkeys.Capture.KeepDuplicate": {
"en": "Keep duplicate",
"ja": "重複のまま割り当てる"
},
"Hotkeys.Capture.ModifierRequired": {
"en": "Include Ctrl, Alt, Shift, or Win",
"ja": "Ctrl・Alt・Shift・Win のどれかと組み合わせてください"
},
"Hotkeys.Capture.NameSeparator": {
"en": "” “",
"ja": "」「"
},
"Hotkeys.Capture.PressKey": {
"en": "Press a key",
"ja": "キーを押してください"
},
"Hotkeys.Capture.Swap": {
"en": "Swap",
"ja": "入れ替える"
},
"Hotkeys.Capture.UseKey": {
"en": "Use this key",
"ja": "このキーにする"
},
"Hotkeys.ChangeAction": {
"en": "Change key for {name}: {chord}",
"ja": "「{name}」のキーを変更: {chord}"
},
"Hotkeys.ClearAction": {
"en": "Remove assignment for {name}",
"ja": "「{name}」の割り当てを外す"
},
"Hotkeys.ConflictsHint": {
"en": "Multiple actions share the same key",
"ja": "同じキーを複数の操作に割り当てています"
},
"Hotkeys.Duplicates.One": {
"en": "{count} duplicate",
"ja": "重複 {count} 件"
},
"Hotkeys.Duplicates.Other": {
"en": "{count} duplicates",
"ja": "重複 {count} 件"
},
"Hotkeys.Empty": {
"en": "No matching actions",
"ja": "該当する操作がありません"
},
"Hotkeys.ResetAction": {
"en": "Reset {name} to default",
"ja": "「{name}」を既定に戻す"
},
"Hotkeys.Showing.One": {
"en": "Showing {count} action",
"ja": "{count} 件を表示"
},
"Hotkeys.Showing.Other": {
"en": "Showing {count} actions",
"ja": "{count} 件を表示"
},
"Hotkeys.Status.Changed": {
"en": "Changed",
"ja": "変更済み"
},
"Hotkeys.Status.Duplicate": {
"en": "Duplicate",
"ja": "重複"
},
"Hotkeys.Status.InUse": {
"en": "In use",
"ja": "ほかのアプリが使用中"
},
"Hotkeys.Status.Unassigned": {
"en": "Not assigned",
"ja": "未割り当て"
},
"Hotkeys_ActionColumn.Text": {
"en": "Action",
"ja": "操作"
},
"Hotkeys_CaptureHint.Text": {
"en": "Select a key field and press your shortcut. Esc cancels; Backspace or Delete removes it. Include Ctrl, Alt, Shift, or Win.",
"ja": "キーの欄を選んで、割り当てたいキーを押します。Esc でやめ、Backspace か Delete で割り当てを外します。Ctrl・Alt・Shift・Win のどれかを含めてください。"
},
"Hotkeys_ConflictHint.Text": {
"en": "Checks for conflicts when you assign keys or open this page. Keys registered as hotkeys by another app are marked “In use”.",
"ja": "ほかのアプリとの競合は、キーを押したときと、このページを開いたときに調べます。ほかのアプリが使っているキーには「ほかのアプリが使用中」と表示します（調べられるのは、ほかのアプリがホットキーとして登録しているキーだけです）。"
},
"Hotkeys_FilterBox.AutomationName": {
"en": "Filter actions",
"ja": "操作を絞り込む"
},
"Hotkeys_FilterBox.PlaceholderText": {
"en": "Filter actions",
"ja": "操作を絞り込む"
},
"Hotkeys_KeyColumn.Text": {
"en": "Key",
"ja": "キー"
},
"Hotkeys_Lead.Text": {
"en": "Click a key field and press a new key to change it. Actions sharing a key are marked “Duplicate”.",
"ja": "キーの欄をクリックして、新しいキーを押すと割り当てを変えられます。同じキーを複数の操作に割り当てている行には「重複」と表示します。"
},
"Hotkeys_Note.Text": {
"en": "Note",
"ja": "注意"
},
"Hotkeys_ResetButton.Content": {
"en": "Reset all to default",
"ja": "すべて既定に戻す"
},
"Hotkeys_StatusColumn.Text": {
"en": "Status",
"ja": "状態"
},
"Hotkeys_Title.Text": {
"en": "Hotkeys",
"ja": "ホットキー"
},
"Hotkeys_WinHint.Text": {
"en": "For shortcuts with Win, Snapwell sends one unassigned key to stop the Start menu from opening.",
"ja": "Win を含むホットキーでは、押したあとにスタート メニューが開かないよう、Snapwell が割り当てのないキーを 1 回送ります"
},
"Language.Auto": {
"en": "Auto (match Windows)",
"ja": "自動（OS に合わせる）"
},
"Language.ChangedNotice": {
"en": "Reopen settings to change this screen's language. The tray menu changes the next time you open it.",
"ja": "設定画面を開き直すと、この画面の言語が切り替わります。トレイのメニューは次に開いたときから切り替わります。"
},
"Language.ChineseSimplified": {
"en": "简体中文",
"ja": "简体中文"
},
"Language.ChineseTraditional": {
"en": "繁體中文",
"ja": "繁體中文"
},
"Language.English": {
"en": "English",
"ja": "English"
},
"Language.Japanese": {
"en": "日本語",
"ja": "日本語"
},
"Launcher.Group.Center": {
"en": "Center",
"ja": "中央"
},
"Launcher.Group.CenterThird": {
"en": "Center third",
"ja": "中央の 1/3"
},
"Launcher.Group.Halves": {
"en": "Halves",
"ja": "上下左右の半分"
},
"Launcher.Group.Maximize": {
"en": "Maximize",
"ja": "最大化"
},
"Launcher.Group.Quarters": {
"en": "Quarters",
"ja": "四隅の 1/4"
},
"Launcher.Group.Thirds": {
"en": "Side thirds",
"ja": "左右の 1/3"
},
"Launcher.Group.TwoThirds": {
"en": "Two-thirds",
"ja": "左右の 2/3"
},
"Launcher.Preset.bl": {
"en": "Bottom-left quarter",
"ja": "左下 1/4"
},
"Launcher.Preset.bottom": {
"en": "Bottom half",
"ja": "下半分"
},
"Launcher.Preset.br": {
"en": "Bottom-right quarter",
"ja": "右下 1/4"
},
"Launcher.Preset.c3": {
"en": "Center third",
"ja": "中央 1/3"
},
"Launcher.Preset.center": {
"en": "Center",
"ja": "中央"
},
"Launcher.Preset.centerKeep": {
"en": "Center (keep size)",
"ja": "中央（サイズ維持）"
},
"Launcher.Preset.l23": {
"en": "Left two-thirds",
"ja": "左 2/3"
},
"Launcher.Preset.l3": {
"en": "Left third",
"ja": "左 1/3"
},
"Launcher.Preset.left": {
"en": "Left half",
"ja": "左半分"
},
"Launcher.Preset.max": {
"en": "Maximize",
"ja": "最大化"
},
"Launcher.Preset.r23": {
"en": "Right two-thirds",
"ja": "右 2/3"
},
"Launcher.Preset.r3": {
"en": "Right third",
"ja": "右 1/3"
},
"Launcher.Preset.right": {
"en": "Right half",
"ja": "右半分"
},
"Launcher.Preset.tl": {
"en": "Top-left quarter",
"ja": "左上 1/4"
},
"Launcher.Preset.top": {
"en": "Top half",
"ja": "上半分"
},
"Launcher.Preset.tr": {
"en": "Top-right quarter",
"ja": "右上 1/4"
},
"Launcher.Showing.One": {
"en": "Showing {count} of {total}",
"ja": "{count} / {total} 件を表示"
},
"Launcher.Showing.Other": {
"en": "Showing {count} of {total}",
"ja": "{count} / {total} 件を表示"
},
"LauncherUi.Arrange.Columns": {
"en": "Side by side",
"ja": "横一列"
},
"LauncherUi.Arrange.Grid": {
"en": "Grid",
"ja": "グリッド"
},
"LauncherUi.Arrange.MainAndStack": {
"en": "Main and stack",
"ja": "メインとスタック"
},
"LauncherUi.Arrange.Rows": {
"en": "Stacked",
"ja": "縦一列"
},
"LauncherUi.Heading": {
"en": "Arrange selected windows",
"ja": "ウィンドウを選んで整列"
},
"LauncherUi.Hint.Close": {
"en": "{keys} Close",
"ja": "{keys} 閉じる"
},
"LauncherUi.Hint.Move": {
"en": "{keys} Move",
"ja": "{keys} 移動"
},
"LauncherUi.Hint.Place": {
"en": "{keys} Place",
"ja": "{keys} 配置"
},
"LauncherUi.Hint.Range": {
"en": "{keys} + arrow keys: range",
"ja": "{keys} + 矢印 範囲"
},
"LauncherUi.Main": {
"en": "Main",
"ja": "メイン"
},
"LauncherUi.Next": {
"en": "Next",
"ja": "次へ"
},
"LauncherUi.NoPresets": {
"en": "No presets to show. Choose some in Settings > Launcher.",
"ja": "表示するプリセットがありません（設定の「ランチャー」で選べます）"
},
"LauncherUi.NoneSelected": {
"en": "None selected",
"ja": "未選択"
},
"LauncherUi.Previous": {
"en": "Previous",
"ja": "前へ"
},
"LauncherUi.Selected": {
"en": "Selected {count} / 9",
"ja": "選択 {count} / 9"
},
"LauncherUi.Selection.One": {
"en": "{columns} × {rows} cell · {width} × {height}",
"ja": "{columns} × {rows} 区画 · {width} × {height}"
},
"LauncherUi.Selection.Other": {
"en": "{columns} × {rows} cells · {width} × {height}",
"ja": "{columns} × {rows} 区画 · {width} × {height}"
},
"LauncherUi.SelectionLimit.One": {
"en": "You can select up to {count} window.",
"ja": "選べるのは最大 {count} 件です。"
},
"LauncherUi.SelectionLimit.Other": {
"en": "You can select up to {count} windows.",
"ja": "選べるのは最大 {count} 件です。"
},
"LauncherUi.Stack": {
"en": "Stack",
"ja": "スタック"
},
"LauncherUi.Target": {
"en": "Target",
"ja": "対象"
},
"LauncherUi.WindowHint": {
"en": "1–9 Toggle selection · Alt+1–4 Arrange",
"ja": "1–9 選択の切り替え · Alt+1–4 整列"
},
"Launcher_ClearAll.Content": {
"en": "Clear all",
"ja": "すべて外す"
},
"Launcher_EmptyHint.Text": {
"en": "No presets to show (choose them in Launcher settings).",
"ja": "表示するプリセットがありません（設定の「ランチャー」で選べます）"
},
"Launcher_Lead.Text": {
"en": "Choose presets to show in the launcher. Presets that don't fit whole cells are unavailable there.",
"ja": "ランチャーに並べるプリセットを選びます。区画の整数倍で作れないものは、ランチャーでは選べません。"
},
"Launcher_PreviewTarget.Text": {
"en": "Target: window    8 × 8",
"ja": "対象　ウィンドウ　　8 × 8"
},
"Launcher_PreviewTitle.Text": {
"en": "Launcher preview",
"ja": "ランチャーでの表示"
},
"Launcher_Reset.Content": {
"en": "Reset to default",
"ja": "既定に戻す"
},
"Launcher_SelectAll.Content": {
"en": "Select all",
"ja": "すべて選ぶ"
},
"Launcher_Title.Text": {
"en": "Launcher",
"ja": "ランチャー"
},
"Look.Preview.Off": {
"en": "Zone preview is off: nothing is shown while dragging",
"ja": "区画のプレビューがオフ: ドラッグ中は何も表示しません"
},
"Look_CornerBox.AutomationName": {
"en": "Window corners",
"ja": "ウィンドウの角"
},
"Look_CornerDefaultDescription.Text": {
"en": "Leaves corners as set by Windows and the app.",
"ja": "何も変えません。角が丸か直角かは、Windows とアプリ次第です"
},
"Look_CornerExamplesHint.Text": {
"en": "Maximized and borderless windows stay unchanged. Use App rules to set corners for individual borderless windows.",
"ja": "最大化中のウィンドウと枠のないウィンドウは変えません。枠のないウィンドウは、アプリごとのルールで個別に指定できます。"
},
"Look_CornerExamplesTitle.Text": {
"en": "Corner examples",
"ja": "ウィンドウの角の見え方"
},
"Look_CornerNoneDescription.Text": {
"en": "Squares the corners of standard windows.",
"ja": "通常のウィンドウを直角にします"
},
"Look_CornerRoundDescription.Text": {
"en": "Rounds standard windows (about 8 px).",
"ja": "通常のウィンドウを標準の丸（約 8 px）にします"
},
"Look_CornerSmallDescription.Text": {
"en": "Uses a smaller radius (about 4 px).",
"ja": "丸みを控えめにします（約 4 px）"
},
"Look_CornersDescription.Text": {
"en": "Uses DWM corner settings. Windows sets the radius (about 8 px).",
"ja": "DWM の角の設定を使います。半径は Windows が決めます（約 8 px）"
},
"Look_CornersTitle.Text": {
"en": "Window corners",
"ja": "ウィンドウの角"
},
"Look_Lead.Text": {
"en": "Choose window corners and the look of Snapwell overlays.",
"ja": "ウィンドウの角の形と、Snapwell のオーバーレイの見た目です。"
},
"Look_OverlayExampleHint.Text": {
"en": "An example throw to the top right. Use the Zone preview and Overlay switches above to see how the display changes.",
"ja": "ウィンドウを右上へ投げた場面の例です。上の「区画のプレビュー」と「オーバーレイ」のスイッチで、表示がどう変わるかを確かめられます。"
},
"Look_OverlayExampleName.AutomationName": {
"en": "Example overlay appearance",
"ja": "オーバーレイの見え方の例"
},
"Look_OverlayExampleTitle.Text": {
"en": "Overlay example",
"ja": "オーバーレイの見え方"
},
"Look_OverlayTitle.Text": {
"en": "Overlay",
"ja": "オーバーレイ"
},
"Look_PredictionLineDescription.Text": {
"en": "Shows a dotted line and dot to the predicted resting point (when Zone preview is on). Turn it off once you're used to throwing.",
"ja": "ウィンドウが止まると予測した位置までを、点線と丸印で示します（区画のプレビューがオンのとき）。慣れたらオフにできます"
},
"Look_PredictionLineTitle.Text": {
"en": "Show prediction line on throw",
"ja": "投げたときに予測の線を表示する"
},
"Look_PredictionLineToggle.AutomationName": {
"en": "Show prediction line on throw",
"ja": "投げたときに予測の線を表示する"
},
"Look_Title.Text": {
"en": "Appearance",
"ja": "外観"
},
"Look_ZoneLinesDescription.Text": {
"en": "Outlines the snap target with dashes (when Zone preview is on). Also applies to cell lines in the launcher.",
"ja": "吸着先の区画の縁に点線の枠を付けます（区画のプレビューがオンのとき）。ランチャーの区画の線にも適用されます"
},
"Look_ZoneLinesTitle.Text": {
"en": "Show cell lines",
"ja": "区画の線を表示する"
},
"Look_ZoneLinesToggle.AutomationName": {
"en": "Show cell lines",
"ja": "区画の線を表示する"
},
"Look_ZonePreviewDescription.Text": {
"en": "Highlights the snap target in blue from release to rest. While resizing, highlights the target until you release (with Snap after resizing on in Grid and gaps). Turn off to hide both.",
"ja": "ウィンドウを投げると、離した瞬間から着地するまで、吸着先の区画を青く塗ります。手動でリサイズするときは、ドラッグしている間ずっと、離したときにそろう区画を塗ります（「分割と間隔」で「手動でリサイズしたら区画にそろえる」をオンにしたとき）。オフにすると、どちらも何も表示しません"
},
"Look_ZonePreviewTitle.Text": {
"en": "Zone preview",
"ja": "区画のプレビュー"
},
"Look_ZonePreviewToggle.AutomationName": {
"en": "Zone preview",
"ja": "区画のプレビュー"
},
"MainWindow.Close.Message": {
"en": "You have unsaved changes. Saving applies them to the running Snapwell.",
"ja": "保存していない変更があります。保存すると、起動中の Snapwell に反映されます。"
},
"MainWindow.Close.Title": {
"en": "Save changes?",
"ja": "変更を保存しますか？"
},
"MainWindow.Status.Changed": {
"en": "You have unsaved changes.",
"ja": "保存していない変更があります"
},
"MainWindow.Status.ReadOnly": {
"en": "Read-only. You can't save these settings.",
"ja": "読み取り専用です。保存できません。"
},
"MainWindow.Status.SavingApplies": {
"en": "Saving applies the changes to the running Snapwell.",
"ja": "保存すると、起動中の Snapwell に反映されます"
},
"MainWindow_Save.Content": {
"en": "Save",
"ja": "保存"
},
"MainWindow_Title.Text": {
"en": "Snapwell settings",
"ja": "Snapwell の設定"
},
"Nav_Backup.AutomationName": {
"en": "Sync and backup",
"ja": "同期とバックアップ"
},
"Nav_Backup.Content": {
"en": "Sync and backup",
"ja": "同期とバックアップ"
},
"Nav_Feel.AutomationName": {
"en": "Feel",
"ja": "操作感"
},
"Nav_Feel.Content": {
"en": "Feel",
"ja": "操作感"
},
"Nav_General.AutomationName": {
"en": "General",
"ja": "一般"
},
"Nav_General.Content": {
"en": "General",
"ja": "一般"
},
"Nav_Grid.AutomationName": {
"en": "Grid and gaps",
"ja": "分割と間隔"
},
"Nav_Grid.Content": {
"en": "Grid and gaps",
"ja": "分割と間隔"
},
"Nav_Hotkeys.AutomationName": {
"en": "Hotkeys",
"ja": "ホットキー"
},
"Nav_Hotkeys.Content": {
"en": "Hotkeys",
"ja": "ホットキー"
},
"Nav_Launcher.AutomationName": {
"en": "Launcher",
"ja": "ランチャー"
},
"Nav_Launcher.Content": {
"en": "Launcher",
"ja": "ランチャー"
},
"Nav_Look.AutomationName": {
"en": "Appearance",
"ja": "外観"
},
"Nav_Look.Content": {
"en": "Appearance",
"ja": "外観"
},
"Nav_Os.AutomationName": {
"en": "Windows features",
"ja": "標準機能"
},
"Nav_Os.Content": {
"en": "Windows features",
"ja": "標準機能"
},
"Nav_Rules.AutomationName": {
"en": "App rules",
"ja": "アプリのルール"
},
"Nav_Rules.Content": {
"en": "App rules",
"ja": "アプリのルール"
},
"Os.Consent.Cancel": {
"en": "Cancel",
"ja": "やめる"
},
"Os.Consent.Message": {
"en": "Turns off Snap windows in Windows Settings while Snapwell is active. This stops snapping by dragging, the Maximize menu, and the Snap bar together. Previous settings are restored when Snapwell is turned off, exits, or next starts after a crash.",
"ja": "Windows の設定アプリの「ウィンドウのスナップ」を、Snapwell が有効な間だけ停止します。ドラッグでのスナップ・最大化ボタンのメニュー・画面上端のスナップ バーが一緒に止まります。Snapwell を無効にしたとき・終了したとき・異常終了のあとに起動したときに、変更前の設定に戻します。"
},
"Os.Consent.Stop": {
"en": "Stop",
"ja": "停止する"
},
"Os.Consent.Title": {
"en": "Stop Windows Snap?",
"ja": "Windows 標準のスナップを停止しますか？"
},
"Os.Diagram.ArrowAfter": {
"en": "The same keys run Snapwell's left, right, top, and bottom half actions (Ctrl+Alt+arrow keys by default). Half sizes follow Grid and gaps settings.",
"ja": "キーはそのまま、Snapwell の「左／右／上／下の半分」（既定では Ctrl+Alt+矢印と同じ操作）が動きます。半分の大きさは、分割と間隔の設定に従います。"
},
"Os.Diagram.ArrowBefore": {
"en": "Moves the window to the left or right half, maximizes it, restores its size, or minimizes it.",
"ja": "ウィンドウを画面の左半分・右半分に寄せる、最大化する、元の大きさに戻す、最小化するといった動作をします。"
},
"Os.Diagram.ArrowName": {
"en": "Win + arrow keys: compare with Windows",
"ja": "Win + 矢印: Windows の標準との違いを図で見る"
},
"Os.Diagram.ArrowReason": {
"en": "Windows reserves these keys. A low-level keyboard hook routes them to Snapwell instead of Windows. Turn this off to restore Windows Win + arrow key behavior.",
"ja": "Windows が予約しているキーのため、低レベルのキーボードフックで受け取り、標準の動作を呼ばずに Snapwell の操作へ渡します。オフにすると、標準の Win + 矢印に戻ります。"
},
"Os.Diagram.BarAfter": {
"en": "No bar appears at the top edge. Moving near it does nothing; Snapwell handles snapping after release with its target preview.",
"ja": "上端に候補のバーは出ません。上端へ近づけても何も起きず、区画への吸着は Snapwell のドラッグ（離した先のプレビュー）で行います。"
},
"Os.Diagram.BarBefore": {
"en": "Dragging a window near the top edge shows a row of layouts at the top center. Release over an option to place the window. The first option suggests placing it with another app.",
"ja": "ウィンドウを持って画面の上端へ近づけると、上端の中央に配置の候補が横一列で出ます。候補の上で離すと、その形の区画に収まります。先頭の候補は、別のアプリと並べる提案です。"
},
"Os.Diagram.BarName": {
"en": "Snap bar: compare with Windows",
"ja": "画面上端のスナップ バー: Windows の標準との違いを図で見る"
},
"Os.Diagram.BarReason": {
"en": "Prevents Windows and Snapwell from competing. Like the Maximize menu, the bar stops together with Snap by dragging and has no separate switch. This feature requires Windows 11 22H2 or later.",
"ja": "Windows と Snapwell の配置が干渉するのを防ぐため。最大化ボタンのメニューと同じく、上の「ドラッグでのスナップ」を止めると一緒に消えるため、個別のスイッチはありません。Windows 11 の 22H2 より前にはない機能です。"
},
"Os.Diagram.DragAfter": {
"en": "No Windows outline appears. Throw and release to highlight the target cells in blue and snap there using springs and magnets. Blue is shown when Zone preview is on. Sizes are not limited to fixed layouts.",
"ja": "標準の枠は出しません。投げて離すと、吸着先の区画を青く表示し、ばねと磁石の物理モデルでその区画に収まります（青い表示は「区画のプレビュー」がオンのとき）。大きさは決まった形に限りません。"
},
"Os.Diagram.DragBefore": {
"en": "Dragging to an edge or corner shows a gray outline. Release to fill a half or quarter, then choose another window for the remaining space. The same switch also stops these suggestions.",
"ja": "端や四隅へ持っていくと、灰色の枠が出ます。離すと、半分か 4 分の 1 の決まった大きさに広がり、続けて残りの場所に並べるウィンドウの候補が出ます（これも同じ設定で止まります）。"
},
"Os.Diagram.DragName": {
"en": "Snap by dragging: compare with Windows",
"ja": "ドラッグでのスナップ: Windows の標準との違いを図で見る"
},
"Os.Diagram.DragReason": {
"en": "Windows Snap and Snapwell would compete to set the window position and size after release. Snapwell turns off the same Snap windows switch as Windows Settings.",
"ja": "標準のスナップと同時に動くと、離したあとにウィンドウの位置と大きさが取り合いになるため。Windows の設定アプリの「ウィンドウのスナップ」と同じスイッチを切ります。"
},
"Os.Diagram.Holding": {
"en": "① While held",
"ja": "① 持っている間"
},
"Os.Diagram.LayoutAfter": {
"en": "Hovering over Maximize shows no menu. Clicking it still maximizes the window. Place windows with the launcher (Ctrl+Alt+Space), hotkeys, or dragging.",
"ja": "最大化ボタンにポインターを載せても、メニューは出ません（ボタンを押すと、通常どおり最大化します）。区画への配置は、ランチャー（Ctrl+Alt+Space）・ホットキー・ドラッグで行います。"
},
"Os.Diagram.LayoutBefore": {
"en": "Hover over Maximize (□) to open a layout menu below it. Choose halves, a 2:1 split, quarters, or another layout to place the window. The first option may show another app's icon to suggest placing them together.",
"ja": "ウィンドウの最大化ボタン（□）にポインターを載せると、右下にメニューが開きます。候補（半分ずつ・2 対 1・左と右の上下分け・4 分割など）から選ぶと、ウィンドウがその形の区画に収まります。先頭の候補には、開いている別のアプリのアイコンが出ます（そのアプリと並べる提案）。"
},
"Os.Diagram.LayoutName": {
"en": "Snap layouts: compare with Windows",
"ja": "スナップ レイアウト: Windows の標準との違いを図で見る"
},
"Os.Diagram.LayoutReason": {
"en": "Windows layouts differ from Snapwell grids and would offer two sets of targets. This menu stops together with Snap by dragging, so it has no separate switch. Registry changes to stop it separately need an Explorer restart.",
"ja": "標準のメニューの形は Snapwell の分割案と別物で、同じウィンドウに 2 通りの配置先が出てしまうため。上の「ドラッグでのスナップ」を止めると一緒に消えるため、この項目に個別のスイッチはありません（レジストリで個別に止めても、エクスプローラーを再起動するまで反映されないため）。"
},
"Os.Diagram.Mechanism": {
"en": "How it works: {reason}",
"ja": "仕組み: {reason}"
},
"Os.Diagram.Released": {
"en": "② After release",
"ja": "② 離したあと"
},
"Os.Diagram.StopReason": {
"en": "Why stop it: {reason}",
"ja": "止める理由: {reason}"
},
"Os.Diagram.Windows": {
"en": "Windows default",
"ja": "Windows の標準"
},
"Os.Diagram.Windows22H2": {
"en": "Windows default (22H2 or later)",
"ja": "Windows の標準（22H2 以降）"
},
"Os.Error.FutureArrow": {
"en": "This device state is from a newer version. Update Snapwell to change Win + arrow keys.",
"ja": "新しいバージョンの端末状態のため、Win + 矢印を変更できません。Snapwell を更新してください。"
},
"Os.Error.FutureRestore": {
"en": "This device state is from a newer version. Update Snapwell to restore Windows behavior.",
"ja": "新しいバージョンの端末状態のため、標準の動作に戻せません。Snapwell を更新してください。"
},
"Os.Error.FutureSnap": {
"en": "This device state is from a newer version. Update Snapwell to change Windows Snap.",
"ja": "新しいバージョンの端末状態のため、標準のスナップを変更できません。Snapwell を更新してください。"
},
"Os.Error.Load": {
"en": "Couldn't read device state. Check the file location and permissions, then reopen settings.",
"ja": "端末状態を読み取れません。ファイルの場所とアクセス権を確認して、開き直してください。"
},
"Os.Error.SaveArrow": {
"en": "Couldn't save the Win + arrow keys choice. Check the file location and permissions.",
"ja": "Win + 矢印の設定を保存できませんでした。ファイルの場所とアクセス権を確認してください。"
},
"Os.Error.SaveRestore": {
"en": "Couldn't save the restore choice. Check the file location and permissions.",
"ja": "標準の動作に戻す設定を保存できませんでした。ファイルの場所とアクセス権を確認してください。"
},
"Os.Error.SaveSnap": {
"en": "Couldn't save the Windows Snap choice. Check the file location and permissions.",
"ja": "標準のスナップの設定を保存できませんでした。ファイルの場所とアクセス権を確認してください。"
},
"Os.Snap.LinkedStopped": {
"en": "Stopped together",
"ja": "連動して停止"
},
"Os.Snap.Replaced": {
"en": "Replaced",
"ja": "置き換え中"
},
"Os.Snap.Stopped": {
"en": "Stopped",
"ja": "停止中"
},
"Os.Snap.Unchanged": {
"en": "Windows default",
"ja": "標準のまま"
},
"OsDiagram_AfterHeading.Text": {
"en": "While Snapwell is active",
"ja": "Snapwell が有効な間"
},
"OsDiagram_BeforeHeading.Text": {
"en": "Windows default",
"ja": "Windows の標準"
},
"OsDiagram_Disclosure.AutomationName": {
"en": "Compare with Windows",
"ja": "Windows の標準との違いを図で見る"
},
"OsDiagram_DisclosureText.Text": {
"en": "Compare with Windows",
"ja": "Windows の標準との違いを図で見る"
},
"Os_ArrowDescription.Text": {
"en": "Replaces these keys with Snapwell actions for left, right, top, and bottom halves.",
"ja": "Snapwell の操作（左・右・上・下の半分）に置き換えます"
},
"Os_ArrowTitle.Text": {
"en": " + arrow keys",
"ja": " + 矢印"
},
"Os_BarDescription.Text": {
"en": "Windows 11 22H2 or later. Stops together with Snap by dragging.",
"ja": "22H2 以降。ドラッグでのスナップを止めると一緒に止まります"
},
"Os_BarTitle.Text": {
"en": "Snap bar at the top of the screen",
"ja": "画面上端のスナップ バー"
},
"Os_DragDescription.Text": {
"en": "Replaces Windows snapping to halves or quarters at the screen edge with Snapwell snapping to cells.",
"ja": "端へ持っていくと半分や 4 分の 1 に広がる標準の動きを、Snapwell の区画への吸着に置き換えます"
},
"Os_DragTitle.Text": {
"en": "Snap by dragging",
"ja": "ドラッグでのスナップ"
},
"Os_LayoutDescription.Text": {
"en": "Stops together with Snap by dragging.",
"ja": "ドラッグでのスナップを止めると一緒に止まります"
},
"Os_LayoutTitle.Text": {
"en": "Snap layouts (Maximize menu)",
"ja": "スナップ レイアウト（最大化ボタンのメニュー）"
},
"Os_Lead.Text": {
"en": "While Snapwell is active, stop Windows placement features or replace them with Snapwell actions. Each section compares the two. Previous values are saved and restored when Snapwell is turned off, exits, or next starts after a crash.",
"ja": "Snapwell が有効な間だけ、Windows の標準の配置機能を止めたり、Snapwell の操作に置き換えたりします。各項目の下に、Windows では何が起きるか・Snapwell ではどうなるかを示します。止める前の値を保存しておき、無効にしたとき・終了したとき・異常終了のあとに起動したときに戻します。"
},
"Os_ReplaceWinArrowToggle.AutomationName": {
"en": "Win + arrow keys",
"ja": "Win + 矢印"
},
"Os_RestoreButton.Content": {
"en": "Restore Windows behavior",
"ja": "標準の動作に戻す"
},
"Os_StandardSnapToggle.AutomationName": {
"en": "Snap by dragging",
"ja": "ドラッグでのスナップ"
},
"Os_Title.Text": {
"en": "Built-in Windows features",
"ja": "Windows の標準機能"
},
"Os_WinArrowTagText.Text": {
"en": "Windows default",
"ja": "標準のまま"
},
"Rule_ClassBox.AutomationName": {
"en": "Window class",
"ja": "ウィンドウ クラス"
},
"Rule_ClassBox.Header": {
"en": "Window class",
"ja": "ウィンドウ クラス"
},
"Rule_CornerBox.AutomationName": {
"en": "Window corners",
"ja": "ウィンドウの角"
},
"Rule_CornerBox.Header": {
"en": "Window corners",
"ja": "ウィンドウの角"
},
"Rule_Dialog.CloseButtonText": {
"en": "Cancel",
"ja": "取り消す"
},
"Rule_Lead.Text": {
"en": "Applies to windows that match every condition. Empty fields are ignored.",
"ja": "指定した条件すべてに一致するウィンドウに適用します。空の欄は条件に含めません。"
},
"Rule_PlacementBox.AutomationName": {
"en": "Placement",
"ja": "配置の扱い"
},
"Rule_PlacementBox.Header": {
"en": "Placement",
"ja": "配置の扱い"
},
"Rule_ProcessBox.AutomationName": {
"en": "Process name",
"ja": "プロセス名"
},
"Rule_ProcessBox.Header": {
"en": "Process name",
"ja": "プロセス名"
},
"Rule_TitleBox.AutomationName": {
"en": "Title (regular expression)",
"ja": "タイトル（正規表現）"
},
"Rule_TitleBox.Header": {
"en": "Title (regular expression)",
"ja": "タイトル（正規表現）"
},
"Rule_TitleBox.PlaceholderText": {
"en": "^Picture-in-picture$",
"ja": "^ピクチャー イン ピクチャー$"
},
"Rule_UwpNotice.Text": {
"en": "UWP apps share a process name. Use the window class or title to identify an app.",
"ja": "UWP アプリはプロセス名ではアプリごとに分けられないので、クラスかタイトルで指定してください。"
},
"Rules.Action.Delete": {
"en": "Delete",
"ja": "削除"
},
"Rules.Action.Down": {
"en": "Move down",
"ja": "下へ"
},
"Rules.Action.Edit": {
"en": "Edit",
"ja": "編集"
},
"Rules.Action.Up": {
"en": "Move up",
"ja": "上へ"
},
"Rules.Condition.Class": {
"en": "Window class {name}",
"ja": "ウィンドウ クラス {name}"
},
"Rules.Condition.Title": {
"en": "Title {pattern}",
"ja": "タイトル {pattern}"
},
"Rules.EditAction": {
"en": "Edit rule: {conditions}",
"ja": "ルールを編集: {conditions}"
},
"Rules.Enabled": {
"en": "On",
"ja": "有効"
},
"Rules.Error.InvalidTitlePattern": {
"en": "The title regular expression is invalid.",
"ja": "タイトルの正規表現が正しくありません。"
},
"Rules.Error.NoCondition": {
"en": "Enter at least one condition.",
"ja": "条件を 1 つ以上入力してください。"
},
"Rules.Error.NoEffect": {
"en": "Set Placement or Window corners to something other than Use global setting.",
"ja": "「配置の扱い」と「ウィンドウの角」の少なくとも一方は、「全体設定に従う」以外にしてください。"
},
"Rules.Error.TooLong": {
"en": "Keep each condition within 260 characters.",
"ja": "条件はそれぞれ 260 文字以内にしてください。"
},
"Rules.ListSeparator": {
"en": ", ",
"ja": "、"
},
"Rules.Match.Count.One": {
"en": "Matching now: {count} window",
"ja": "いま一致するウィンドウ: {count} 件"
},
"Rules.Match.Count.Other": {
"en": "Matching now: {count} windows",
"ja": "いま一致するウィンドウ: {count} 件"
},
"Rules.Match.More.One": {
"en": "{count} other window",
"ja": "ほか {count} 件"
},
"Rules.Match.More.Other": {
"en": "{count} other windows",
"ja": "ほか {count} 件"
},
"Rules.Match.WithTitles": {
"en": "{summary} ({names})",
"ja": "{summary}（{names}）"
},
"Rules.Placement.Exclude": {
"en": "Excluded",
"ja": "対象外"
},
"Rules.Placement.ForceTarget": {
"en": "Always include",
"ja": "強制的に対象"
},
"Rules.Placement.PositionOnly": {
"en": "Position only",
"ja": "位置だけ"
},
"Rules_AddButton.AutomationName": {
"en": "Add rule",
"ja": "ルールを追加"
},
"Rules_AddButton.Content": {
"en": "Add rule",
"ja": "ルールを追加"
},
"Rules_ConditionColumn.Text": {
"en": "Condition",
"ja": "条件"
},
"Rules_CornerColumn.Text": {
"en": "Corners",
"ja": "角"
},
"Rules_EmptyHint.Text": {
"en": "No rules yet. Use Add rule to create one.",
"ja": "ルールはまだありません。「ルールを追加」から作れます。"
},
"Rules_EnabledColumn.Text": {
"en": "On",
"ja": "有効"
},
"Rules_ExclusionHint.Text": {
"en": "Rules cannot include these windows, even with Always include.",
"ja": "次のウィンドウは、ルールでは変えられません（「強制的に対象」にしても対象になりません）。"
},
"Rules_Exclusions.AutomationName": {
"en": "Always excluded by Snapwell",
"ja": "Snapwell が最初から対象外にしているもの"
},
"Rules_Exclusions.Header": {
"en": "Always excluded by Snapwell",
"ja": "Snapwell が最初から対象外にしているもの"
},
"Rules_HiddenExclusion.Text": {
"en": "• Windows hidden by the system, including minimized windows and windows on other virtual desktops",
"ja": "• 表示されていない・最小化されている・別の仮想デスクトップにあるなど、システムに隠された（クローク中の）ウィンドウ"
},
"Rules_Lead.Text": {
"en": "Set exceptions for an app: exclude it, move it without resizing, or change its corners. Rules are checked from top to bottom. Placement and corners each use the first rule that specifies them.",
"ja": "特定のアプリのウィンドウについて、対象から外す・位置だけを動かす・ウィンドウの角を変える、といった例外を決められます。上のルールから順に調べ、「配置」と「角」はそれぞれ、指定のある最初のルールを使います。"
},
"Rules_LimitHint.Text": {
"en": "Up to 200 rules. Delete a rule before adding another.",
"ja": "ルールは 200 件までです。追加するには、先にルールを削除してください。"
},
"Rules_OwnExclusion.Text": {
"en": "• Snapwell windows and child windows",
"ja": "• Snapwell 自身のウィンドウ、子ウィンドウ"
},
"Rules_PickButton.AutomationName": {
"en": "Add from window",
"ja": "ウィンドウを選んで追加"
},
"Rules_PickButton.Content": {
"en": "Add from window",
"ja": "ウィンドウを選んで追加"
},
"Rules_PlacementColumn.Text": {
"en": "Placement",
"ja": "配置"
},
"Rules_ShellExclusion.Text": {
"en": "• Shell windows such as the desktop, taskbar, Start menu, and hidden notification area icons",
"ja": "• デスクトップ、タスクバー、スタート メニューなどのシェル、通知領域の隠れたアイコンの一覧"
},
"Rules_Title.Text": {
"en": "App rules",
"ja": "アプリごとのルール"
},
"Session.Load.Defaults": {
"en": "The settings file was damaged. Showing defaults.",
"ja": "設定ファイルが壊れていたため、既定値を表示しています。"
},
"Session.Load.Failed": {
"en": "Can't read the settings file. Check the file location and permissions, then reopen settings.",
"ja": "設定ファイルを読み取れません。ファイルの場所とアクセス権を確認して、開き直してください。"
},
"Session.Load.Future": {
"en": "These settings are from a newer version, so you can't save here. Update Snapwell.",
"ja": "新しいバージョンの設定のため、この画面では保存できません。Snapwell を更新してください。"
},
"Session.Save.Busy": {
"en": "Sync is running. Wait a moment, then save again.",
"ja": "同期の実行中のため、少し待ってからもう一度保存してください。"
},
"Session.Save.Failed": {
"en": "Couldn't save. Check the file location and permissions, then save again.",
"ja": "保存できませんでした。ファイルの場所とアクセス権を確認して、もう一度保存してください。"
},
"Session.Save.Future": {
"en": "The settings changed to a newer version, so you can't save. Update Snapwell and reopen settings.",
"ja": "新しいバージョンの設定に変わったため、保存できません。Snapwell を更新して開き直してください。"
},
"Session.Save.InvalidRules": {
"en": "Some app rules can't be saved. Fix their conditions or delete them on the App rules page.",
"ja": "アプリごとのルールに保存できないものがあります。「アプリのルール」で条件を直すか、削除してください。"
},
"Session.Save.ReadOnly": {
"en": "Can't save read-only settings.",
"ja": "この設定は読み取り専用のため、保存できません。"
},
"Session.Save.Saved": {
"en": "Saved. Changes apply immediately if Snapwell is running.",
"ja": "保存しました。Snapwell が起動していれば、すぐに反映されます。"
},
"Session.Sync.Future": {
"en": "These settings are from a newer version, so you can't sync. Update Snapwell.",
"ja": "新しいバージョンの設定のため同期できません。Snapwell を更新してください。"
},
"Session.Sync.Imported": {
"en": "Imported the settings from the sync folder.",
"ja": "同期先の設定を取り込みました。"
},
"Session.Sync.KeepEdits": {
"en": "Imported the settings from the sync folder. Saving your unsaved changes will overwrite them.",
"ja": "同期先の設定を取り込みました。未保存の変更をそのまま保存すると、取り込んだ内容を上書きします。"
},
"Session.Sync.ReloadFailed": {
"en": "Couldn't reload the imported settings. Reopen settings.",
"ja": "取り込んだ設定を再読み込みできませんでした。画面を開き直してください。"
},
"Setup.Count": {
"en": "{step} / 4",
"ja": "{step} / 4"
},
"Setup.Done": {
"en": "Done",
"ja": "完了"
},
"Setup.Error.Load": {
"en": "Can't read or write settings. Check the file location, permissions, and Snapwell version, then reopen setup.",
"ja": "設定を読み書きできません。ファイルの場所とアクセス権、Snapwell のバージョンを確認して開き直してください。"
},
"Setup.Error.ReadOnly": {
"en": "These settings are read-only. Update Snapwell and reopen setup to save.",
"ja": "この設定は読み取り専用のため、保存できません。Snapwell を更新して開き直してください。"
},
"Setup.Error.Save": {
"en": "Couldn't save settings. Check the file location and permissions, then try again.",
"ja": "保存できませんでした。ファイルの場所とアクセス権を確認して、もう一度保存してください。"
},
"Setup.Error.SaveFallback": {
"en": "Couldn't save settings.",
"ja": "保存できませんでした。"
},
"Setup.Grid.AccessiblePlan": {
"en": "{name} {description}",
"ja": "{name} {description}"
},
"Setup.Grid.Gap": {
"en": "Window gap",
"ja": "ウィンドウの間隔"
},
"Setup.Grid.GapName": {
"en": "Window gap (px)",
"ja": "ウィンドウの間隔 (px)"
},
"Setup.Grid.Lead": {
"en": "A cell is the basic unit of placement. You can change the grid for each monitor later.",
"ja": "1 区画が配置の基本単位です。あとからモニターごとに変えられます。"
},
"Setup.Grid.NotSelected": {
"en": "None selected",
"ja": "未選択"
},
"Setup.Grid.Pixels": {
"en": "{value} px",
"ja": "{value} px"
},
"Setup.Grid.PlanName": {
"en": "{columns} × {rows}",
"ja": "{columns} × {rows}"
},
"Setup.Grid.Selected": {
"en": "Selected",
"ja": "選択中"
},
"Setup.Grid.Title": {
"en": "Choose a grid",
"ja": "分割案を選ぶ"
},
"Setup.HeadingName": {
"en": "{count} {heading}",
"ja": "{count} {heading}"
},
"Setup.Hotkeys.ArrowsTemplate": {
"en": "{keys} + arrow keys / U I J K",
"ja": "{keys} + 矢印 / U I J K"
},
"Setup.Hotkeys.Available": {
"en": "Available",
"ja": "使えます"
},
"Setup.Hotkeys.Conflict": {
"en": "Duplicate",
"ja": "重複"
},
"Setup.Hotkeys.Halves": {
"en": "Halves and quarters",
"ja": "半分・四分割"
},
"Setup.Hotkeys.Hint": {
"en": "You can change conflicting keys later in Hotkeys settings.",
"ja": "重なっているキーは、あとで設定の「ホットキー」で変えられます。"
},
"Setup.Hotkeys.Launcher": {
"en": "Open launcher",
"ja": "ランチャーを開く"
},
"Setup.Hotkeys.Lead": {
"en": "Checked for conflicts with hotkeys on this PC.",
"ja": "この PC で使われているキーと重ならないかを確かめました。"
},
"Setup.Hotkeys.Title": {
"en": "Check hotkeys",
"ja": "ホットキーの確認"
},
"Setup.Hotkeys.Unassigned": {
"en": "Not assigned",
"ja": "未割り当て"
},
"Setup.Intro.AutomationName": {
"en": "Windows burst, dance, and settle into zones in an intro animation",
"ja": "ウィンドウが弾けて踊り、区画へ並んで止まる導入アニメーション"
},
"Setup.Next": {
"en": "Next",
"ja": "次へ"
},
"Setup.Plan.12x6": {
"en": "Also supports thirds and two-thirds.",
"ja": "1/3 や 2/3 も作れます"
},
"Setup.Plan.6x4": {
"en": "Larger cells for quick placement.",
"ja": "大きな区画で手早く"
},
"Setup.Plan.8x8": {
"en": "Recommended. Halves and quarters.",
"ja": "おすすめ。半分・四分割を作れます"
},
"Setup.Standard.ArrowsTemplate": {
"en": "{keys} + arrow keys",
"ja": "{keys} + 矢印"
},
"Setup.Standard.Consent": {
"en": "I understand that Snapwell saves the original values and restores them when turned off, on exit, or on restart after a crash.",
"ja": "Snapwell が変更前の値を保存し、無効にしたとき・終了したとき・異常終了のあとに起動したときに元へ戻すことを理解しました。"
},
"Setup.Standard.Drag": {
"en": "Snap while dragging",
"ja": "ドラッグでのスナップ"
},
"Setup.Standard.Hint": {
"en": "You can keep Windows Snap on. Both Windows and Snapwell will then snap windows when you drag.",
"ja": "止めない場合も Snapwell は使えます。その場合は、ドラッグすると Windows と Snapwell のスナップが両方動きます。"
},
"Setup.Standard.Layouts": {
"en": "Snap layouts and Snap bar",
"ja": "スナップ レイアウト・スナップ バー"
},
"Setup.Standard.Lead": {
"en": "Windows Snap can compete with Snapwell. While Snapwell is active, it can make these changes.",
"ja": "Windows のスナップと同時に動くと、位置が取り合いになります。Snapwell が有効な間だけ、次の設定を変えます。"
},
"Setup.Standard.OnToOff": {
"en": "On → Off",
"ja": "オン → オフ"
},
"Setup.Standard.Replace": {
"en": "Replaced with Snapwell actions",
"ja": "Snapwell の操作に置き換え"
},
"Setup.Standard.Title": {
"en": "Turn off Windows Snap?",
"ja": "標準のスナップを止めますか？"
},
"Setup.Step.Grid": {
"en": "Grid",
"ja": "分割案"
},
"Setup.Step.Hotkeys": {
"en": "Hotkeys",
"ja": "ホットキー"
},
"Setup.Step.StandardFeatures": {
"en": "Windows features",
"ja": "標準機能"
},
"Setup.Step.Welcome": {
"en": "Welcome",
"ja": "ようこそ"
},
"Setup.Welcome.Body": {
"en": "Grab a window and throw it. It is pulled into the nearest cell and stops there.\nNext, you will set up standard features, hotkeys, and layout.",
"ja": "つかんで投げると、いちばん近い区画に吸い込まれて止まります。\nこのあと、標準機能、ホットキー、分割案の 3 つを設定します。"
},
"Setup.Welcome.Hint": {
"en": "The window above moves. Grab it and give it a throw.",
"ja": "上のウィンドウは動かせます。つかんで投げてみてください。"
},
"Setup.Welcome.HintAgain": {
"en": "Once more.",
"ja": "もう一度どうぞ。"
},
"Setup.Welcome.HintHeld": {
"en": "Let go and it settles into the nearest cell.",
"ja": "離すと、近い区画へ収まります。"
},
"Setup.Welcome.LauncherTemplate": {
"en": "Open the launcher with {keys} or from the tray menu.",
"ja": "{keys}、またはトレイのメニューでランチャーを開きます"
},
"Setup.Welcome.Lead": {
"en": "Throw a window to snap it to nearby cells. You can also use hotkeys or the launcher.",
"ja": "ウィンドウを投げると、近い区画に吸い込まれて止まります。ホットキーとランチャーでも配置できます。"
},
"Setup.Welcome.Start": {
"en": "Get started",
"ja": "はじめる"
},
"Setup.Welcome.Title": {
"en": "Throw a window.",
"ja": "ウィンドウを投げよう。"
},
"Setup_Back.Content": {
"en": "Back",
"ja": "戻る"
},
"Setup_UpdateDescription.Text": {
"en": "When enabled, Snapwell connects to GitHub to fetch only the latest version information. Only a User-Agent containing the version is sent; no identifiers are sent. Off by default.",
"ja": "オンにすると GitHub に接続して最新バージョンの情報だけを取得します。送るのはバージョンを含む User-Agent だけで、識別子は送りません。既定はオフです。"
},
"Setup_UpdateTitle.Text": {
"en": "Updates",
"ja": "更新"
},
"Setup_UpdateToggle.AutomationName": {
"en": "Check for updates automatically",
"ja": "更新を自動で確認する"
},
"Status.Accessibility.Item": {
"en": "Snapwell, {name}",
"ja": "Snapwell、{name}"
},
"Status.Accessibility.MenuDisabled.One": {
"en": "Snapwell menu, inactive, {count} monitor",
"ja": "Snapwell メニュー、無効、モニター構成: {count} 画面"
},
"Status.Accessibility.MenuDisabled.Other": {
"en": "Snapwell menu, inactive, {count} monitors",
"ja": "Snapwell メニュー、無効、モニター構成: {count} 画面"
},
"Status.Accessibility.MenuEnabled.One": {
"en": "Snapwell menu, active, {count} monitor",
"ja": "Snapwell メニュー、有効、モニター構成: {count} 画面"
},
"Status.Accessibility.MenuEnabled.Other": {
"en": "Snapwell menu, active, {count} monitors",
"ja": "Snapwell メニュー、有効、モニター構成: {count} 画面"
},
"Status.Accessibility.ToggleOff": {
"en": "Snapwell, enable switch, off",
"ja": "Snapwell、有効スイッチ、オフ"
},
"Status.Accessibility.ToggleOn": {
"en": "Snapwell, enable switch, on",
"ja": "Snapwell、有効スイッチ、オン"
},
"Status.DeviceReloadFailure": {
"en": "Device settings reload failed; retry on next change.",
"ja": "端末状態を再読み込みできませんでした。次の変更で再試行します"
},
"Status.DeviceReloadRetry": {
"en": "Couldn't reload device settings. Retrying.",
"ja": "端末状態を再読み込みできません。再試行しています"
},
"Status.DeviceSaveFailure": {
"en": "Device settings unsaved; changes last until exit.",
"ja": "端末状態を保存できませんでした。変更はこの起動中だけ有効です"
},
"Status.FutureSettings": {
"en": "Protecting settings from a newer version",
"ja": "新しいバージョンの設定を保護しています"
},
"Status.HookFailure": {
"en": "Couldn't start input hooks. Using Windows Snap.",
"ja": "入力フックを開始できません。Windows 標準のスナップを使用します"
},
"Status.ReloadFailure": {
"en": "Couldn't apply settings. Using the previous settings.",
"ja": "設定を反映できませんでした。前の設定を使用しています"
},
"Status.SettingsLaunchFailure": {
"en": "Couldn't open settings.",
"ja": "設定画面を起動できませんでした。"
},
"Status.SettingsMissing": {
"en": "Snapwell.Settings.exe missing; put beside Snapwell.",
"ja": "設定画面 (Snapwell.Settings.exe) が見つかりません。Snapwell と同じフォルダーに置いてください。"
},
"Status.SettingsReset": {
"en": "Damaged settings backed up; using defaults.",
"ja": "壊れた設定を退避し、既定値で起動しました"
},
"Status.SnapDisableFailure": {
"en": "Couldn't turn off Windows Snap.",
"ja": "Windows 標準のスナップを停止できませんでした"
},
"Status.SnapDisableUnconfirmed": {
"en": "Couldn't confirm that Windows Snap is off.",
"ja": "Windows 標準のスナップの停止を確認できませんでした"
},
"Status.SnapRestoreFailure": {
"en": "Couldn't restore Windows Snap. Will retry on restart.",
"ja": "Windows 標準のスナップを復元できませんでした。再起動時に再試行します"
},
"Status.SyncAccessFailure": {
"en": "Couldn't access the sync folder or device settings.",
"ja": "同期フォルダーまたは端末状態を読み書きできません。"
},
"Transfer.Anchor.Center": {
"en": "Center",
"ja": "中心"
},
"Transfer.Anchor.NearEdge": {
"en": "Nearest edge",
"ja": "近い辺"
},
"Transfer.Anchor.TopLeft": {
"en": "Top left",
"ja": "左上"
},
"Transfer.Corner.Default": {
"en": "Windows default",
"ja": "Windows の既定"
},
"Transfer.Corner.DoNotRound": {
"en": "Square corners",
"ja": "角丸なし"
},
"Transfer.Corner.Round": {
"en": "Force rounded corners",
"ja": "角丸を強制する"
},
"Transfer.Corner.RoundSmall": {
"en": "Small rounded corners",
"ja": "小さい角丸"
},
"Transfer.Diff.CornerPreference": {
"en": "Window corners",
"ja": "ウィンドウの角"
},
"Transfer.Diff.Edge": {
"en": "Edge margin",
"ja": "画面端の余白"
},
"Transfer.Diff.Feel": {
"en": "Feel",
"ja": "操作感"
},
"Transfer.Diff.Gap": {
"en": "Window gap",
"ja": "ウィンドウの間隔"
},
"Transfer.Diff.Grid": {
"en": "Grid",
"ja": "分割案"
},
"Transfer.Diff.Hotkey": {
"en": "{name}",
"ja": "{name}"
},
"Transfer.Diff.LauncherPresets": {
"en": "Launcher presets",
"ja": "ランチャーのプリセット"
},
"Transfer.Diff.MagnetStrength": {
"en": "Magnet strength",
"ja": "磁石の強さ"
},
"Transfer.Diff.MonitorGrids": {
"en": "Per-monitor grids",
"ja": "モニター別の分割案"
},
"Transfer.Diff.MoveFixedSizeWindows": {
"en": "Move fixed-size windows",
"ja": "サイズ固定のウィンドウも動かす"
},
"Transfer.Diff.MovePopupWindows": {
"en": "Move pop-ups and flyouts",
"ja": "ポップアップとフライアウトも動かす"
},
"Transfer.Diff.ReduceMotion": {
"en": "Reduce motion",
"ja": "動きを減らす"
},
"Transfer.Diff.ResizeAnchor": {
"en": "Resize anchor",
"ja": "拡大・縮小で固定する位置"
},
"Transfer.Diff.ResizeStep": {
"en": "Resize step",
"ja": "1 回の拡大・縮小の幅"
},
"Transfer.Diff.ResizeTiming": {
"en": "Size change timing",
"ja": "サイズの変え方"
},
"Transfer.Diff.ShowPredictionLine": {
"en": "Prediction line",
"ja": "予測の線"
},
"Transfer.Diff.ShowZoneLines": {
"en": "Cell lines",
"ja": "区画の線"
},
"Transfer.Diff.ShowZonePreview": {
"en": "Zone preview",
"ja": "区画のプレビュー"
},
"Transfer.Diff.SnapAfterResize": {
"en": "Snap after resizing",
"ja": "手動でリサイズしたら区画にそろえる"
},
"Transfer.Diff.SnapMode": {
"en": "Snap target",
"ja": "吸着の方式"
},
"Transfer.Diff.SnapRadius": {
"en": "Snap radius",
"ja": "吸着半径"
},
"Transfer.Diff.WindowRules": {
"en": "App rules",
"ja": "アプリごとのルール"
},
"Transfer.Error.Empty": {
"en": "Can't import empty settings.",
"ja": "設定が空のため、取り込めません。"
},
"Transfer.Error.Future": {
"en": "These settings are from a newer version. Update Snapwell to import them.",
"ja": "新しいバージョンの設定のため、取り込めません。Snapwell を更新してください。"
},
"Transfer.Error.Invalid": {
"en": "Can't import this settings file. It is empty or damaged.",
"ja": "設定ファイルが空か壊れているため、取り込めません。"
},
"Transfer.Error.NotObject": {
"en": "No settings JSON object was found.",
"ja": "設定の JSON オブジェクトがありません。"
},
"Transfer.Error.Read": {
"en": "Can't read the settings file. Check the file location and permissions.",
"ja": "設定ファイルを読み取れません。ファイルの場所とアクセス権を確認してください。"
},
"Transfer.Mode.Corners": {
"en": "Corners",
"ja": "四隅"
},
"Transfer.Mode.Zones": {
"en": "Cells",
"ja": "区画"
},
"Transfer.Timing.OnLand": {
"en": "Change on landing",
"ja": "着地時に変える"
},
"Transfer.Timing.WhileMoving": {
"en": "Change while moving",
"ja": "移動しながら変える"
},
"Transfer.Value.Cells.One": {
"en": "{step} cell",
"ja": "{step} 区画"
},
"Transfer.Value.Cells.Other": {
"en": "{step} cells",
"ja": "{step} 区画"
},
"Transfer.Value.CustomFeel": {
"en": "Custom (based on {feel}; mass {mass}, frequency {frequency}, damping ratio {damping})",
"ja": "カスタム（基準: {feel}、質量 {mass}、固有振動数 {frequency}、減衰比 {damping}）"
},
"Transfer.Value.FollowFeel": {
"en": "Use feel setting",
"ja": "操作感に従う"
},
"Transfer.Value.Grid": {
"en": "{columns} × {rows}",
"ja": "{columns} × {rows}"
},
"Transfer.Value.Monitor": {
"en": "{name}: {columns} × {rows}, gap {gap}, edge margin {edge}",
"ja": "{name}: {columns} × {rows}, 間隔 {gap}, 余白 {edge}"
},
"Transfer.Value.Off": {
"en": "Off",
"ja": "オフ"
},
"Transfer.Value.On": {
"en": "On",
"ja": "オン"
},
"Transfer.Value.Pixels": {
"en": "{value} px",
"ja": "{value} px"
},
"Transfer.Value.Preset": {
"en": "Use preset",
"ja": "プリセットのまま"
},
"Transfer.Value.Rule": {
"en": "{enabled}: {process}, {class}, {pattern}, {placement}, {corner}",
"ja": "{enabled}: {process}, {class}, {pattern}, {placement}, {corner}"
},
"Transfer.Value.Unassigned": {
"en": "Not assigned",
"ja": "未割り当て"
},
"Transfer_Dialog.CloseButtonText": {
"en": "Cancel",
"ja": "取り消す"
},
"Transfer_Dialog.PrimaryButtonText": {
"en": "Replace all",
"ja": "全体を置き換える"
},
"Transfer_Dialog.SecondaryButtonText": {
"en": "Selected items only",
"ja": "選んだ項目だけ"
},
"Transfer_Dialog.Title": {
"en": "Review settings to import",
"ja": "取り込む設定の確認"
},
"Transfer_Lead.Text": {
"en": "These settings differ from your current settings. Select items to import only those changes.",
"ja": "いまの設定との違いです。一部だけ取り込む場合は、項目を選んでください。"
},
"Transfer_SaveHint.Text": {
"en": "Importing does not save changes. Use Save at the bottom of the settings window to apply them.",
"ja": "取り込むだけでは保存されません。画面下の「保存」で確定します。"
},
"Tray.Arrange": {
"en": "Arrange this monitor",
"ja": "現在のモニターを整列"
},
"Tray.Exit": {
"en": "Exit and restore Windows",
"ja": "終了（標準の動作に戻す）"
},
"Tray.Launcher": {
"en": "Open launcher",
"ja": "ランチャーを開く"
},
"Tray.Monitors.One": {
"en": "Monitor: {count}",
"ja": "モニター構成: {count} 画面"
},
"Tray.Monitors.Other": {
"en": "Monitors: {count}",
"ja": "モニター構成: {count} 画面"
},
"Tray.Settings": {
"en": "Open settings",
"ja": "設定を開く"
},
"Tray.Status.Active": {
"en": "Active",
"ja": "有効"
},
"Tray.Status.Error": {
"en": "Error",
"ja": "異常"
},
"Tray.Status.Paused": {
"en": "Paused",
"ja": "一時停止中"
},
"Tray.Tip.Attention": {
"en": "Snapwell: {status}",
"ja": "Snapwell: {status}"
},
"Tray.Tip.Paused": {
"en": "Snapwell (paused)",
"ja": "Snapwell（一時停止中）"
},
"Tray.UpdateAvailable": {
"en": "Version {version} is available",
"ja": "新しいバージョン {version} があります"
},
"Update.Cancel.Short": {
"en": "Cancel",
"ja": "取り消す"
},
"Update.Download.Cancelled": {
"en": "The update download was cancelled.",
"ja": "更新のダウンロードを取り消しました。"
},
"Update.Download.HashMismatch": {
"en": "The downloaded file hash doesn't match. Try again.",
"ja": "ダウンロードしたファイルのハッシュが一致しません。もう一度お試しください。"
},
"Update.Download.HttpStatus": {
"en": "The download server returned an error. Try again later.",
"ja": "配布先がエラーを返しました。後でもう一度お試しください。"
},
"Update.Download.InsecureRedirect": {
"en": "The download stopped because the redirect destination was unsafe.",
"ja": "安全でない転送先のため、ダウンロードを中止しました。"
},
"Update.Download.IoError": {
"en": "Couldn't save the downloaded file. Check free space and permissions.",
"ja": "ダウンロードしたファイルを保存できませんでした。空き容量とアクセス権を確認してください。"
},
"Update.Download.NetworkError": {
"en": "Couldn't download the update. Check your network connection.",
"ja": "更新をダウンロードできませんでした。ネットワーク接続を確認してください。"
},
"Update.Download.SizeMismatch": {
"en": "The downloaded file size doesn't match. Try again.",
"ja": "ダウンロードしたファイルのサイズが一致しません。もう一度お試しください。"
},
"Update.Download.Timeout": {
"en": "The update download timed out. Try again.",
"ja": "更新のダウンロードがタイムアウトしました。もう一度お試しください。"
},
"Update.Downloading.Title": {
"en": "Downloading version {version}",
"ja": "新しいバージョン {version} をダウンロードしています"
},
"Update.InstallBlock.AnotherUpdateRunning": {
"en": "Another update is in progress. Wait for it to finish.",
"ja": "別の更新が進行中です。完了するまでお待ちください。"
},
"Update.InstallBlock.ArchitectureUnsupported": {
"en": "This architecture can't update in the app.",
"ja": "このアーキテクチャーではアプリ内で更新できません。"
},
"Update.InstallBlock.DevelopmentBuild": {
"en": "Development builds can't update in the app.",
"ja": "開発用ビルドではアプリ内で更新できません。"
},
"Update.InstallBlock.DownloadForbidden": {
"en": "This package can't be downloaded in the app.",
"ja": "この配布ファイルはアプリ内でダウンロードできません。"
},
"Update.InstallBlock.InsufficientSpace": {
"en": "There is not enough free space to update. Free up at least twice the package size.",
"ja": "更新に必要な空き容量がありません。配布ファイルの 2 倍以上の空きを確保してください。"
},
"Update.InstallBlock.MissingCache": {
"en": "Update information or its signature is missing. Click Check now to check again.",
"ja": "更新情報か署名が保存されていません。「今すぐ確認」で確認してください。"
},
"Update.InstallBlock.MissingExecutable": {
"en": "Snapwell.exe isn't in the same folder as Settings, so the app can't update.",
"ja": "設定画面と同じフォルダーに Snapwell.exe がないため、アプリ内で更新できません。"
},
"Update.InstallBlock.NoOffer": {
"en": "No update is available.",
"ja": "利用できる更新はありません。"
},
"Update.NotificationTitle": {
"en": "Snapwell",
"ja": "Snapwell"
},
"Update.Progress": {
"en": "{downloaded} / {total} MB",
"ja": "{downloaded} / {total} MB"
},
"Update.Result.ApplyRolledBack": {
"en": "The update couldn't be applied, so the previous version was restored.",
"ja": "更新を適用できなかったため、元のバージョンに戻しました。"
},
"Update.Result.Architecture": {
"en": "The downloaded version doesn't support this architecture.",
"ja": "ダウンロードしたバージョンは、このアーキテクチャーに対応していません。"
},
"Update.Result.Busy": {
"en": "Another update is running. Wait for it to finish and try again.",
"ja": "別の更新が進行中です。完了してからもう一度お試しください。"
},
"Update.Result.Cancelled": {
"en": "Update approval was cancelled.",
"ja": "更新の承認を取り消しました。"
},
"Update.Result.Failed": {
"en": "Couldn't update (code {code}).",
"ja": "更新できませんでした (コード {code})。"
},
"Update.Result.Installed": {
"en": "Updated to version {version}.",
"ja": "更新しました (バージョン {version})。"
},
"Update.Result.InvalidManifest": {
"en": "The update information or signature is invalid. Click Check now to check again.",
"ja": "更新情報か署名が正しくありません。「今すぐ確認」で確認し直してください。"
},
"Update.Result.InvalidPackage": {
"en": "The downloaded package is invalid. Try again.",
"ja": "ダウンロードした配布ファイルの内容が正しくありません。もう一度お試しください。"
},
"Update.Result.IoError": {
"en": "Couldn't access the update files. Check the file location, free space, and permissions.",
"ja": "更新のファイルを操作できませんでした。ファイルの場所、空き容量、アクセス権を確認してください。"
},
"Update.Result.NotNewer": {
"en": "The downloaded version isn't newer than the current version.",
"ja": "ダウンロードしたバージョンは、現在のバージョンより新しくありません。"
},
"Update.Result.Reinstall": {
"en": "Snapwell may be damaged. Reinstall it from the releases page.",
"ja": "Snapwell が正常な状態でない可能性があるため、リリースのページから入れ直してください。"
},
"Update.Result.RolledBack": {
"en": "The new version didn't start, so the previous version was restored.",
"ja": "新しいバージョンが起動しなかったため、元のバージョンに戻しました。"
},
"Update.StartFailed": {
"en": "Couldn't start the update. Try again.",
"ja": "更新を開始できませんでした。もう一度お試しください。"
},
"Update.Startup.AnotherUpdateRunning": {
"en": "Another update is in progress. Wait for it to finish.",
"ja": "別の更新が進行中です。完了するまでお待ちください。"
},
"Update_Cancel.AutomationName": {
"en": "Cancel update download",
"ja": "更新のダウンロードを取り消す"
},
"Update_Install.AutomationName": {
"en": "Update and restart",
"ja": "更新して再起動"
},
"Update_Install.Content": {
"en": "Update and restart",
"ja": "更新して再起動"
},
"Update_Progress.AutomationName": {
"en": "Update download progress",
"ja": "更新のダウンロードの進捗"
},
"Update_ReinstallLink.AutomationName": {
"en": "Open the Snapwell releases page",
"ja": "Snapwell のリリースのページを開く"
},
"Update_ReinstallLink.Content": {
"en": "Open releases page",
"ja": "リリースのページを開く"
},
"Window.SettingsTitle": {
"en": "Snapwell settings",
"ja": "Snapwell の設定"
},
"Window.SetupTitle": {
"en": "Snapwell setup",
"ja": "Snapwell の初回セットアップ"
},
"WindowPicker_Dialog.CloseButtonText": {
"en": "Cancel",
"ja": "取り消す"
},
"WindowPicker_Dialog.Title": {
"en": "Add from window",
"ja": "ウィンドウを選んで追加"
},
"WindowPicker_EmptyHint.Text": {
"en": "No windows are available to select.",
"ja": "いま選べるウィンドウはありません。"
},
"WindowPicker_Lead.Text": {
"en": "Choose an open window. Add rule will open with its details filled in.",
"ja": "いま開いているウィンドウから選びます。選ぶと、その値が入った状態で「ルールを追加」が開きます。"
}
};
