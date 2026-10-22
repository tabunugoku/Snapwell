# Snapwell 用語集（日本語・English・简体中文・繁體中文）

画面の文言を訳すときの基準。設計は `docs/I18N.md`。
語は、2026-10-22 の main（f65e5bf）の画面の文言（XAML、Core・App・Settings の文字列）から拾った。

- **決定済み（2026-10-22）**: 訳語と表記は、すべて各行の推奨で決まった。中国語の対応は中断中だが、将来の再開のため欄を残す。
- **要確認**: Windows の画面の名前を引用している語。各言語の Windows の表記に合わせる必要があり、実際の Windows で確かめるまで仮の訳。
- 繁體中文は台湾の語彙と表記（例: 軟體、設定、視窗、資料夾、檔案、螢幕、顯示器）。简体中文は中国大陸の語彙（软件、设置、窗口、文件夹、文件、屏幕、显示器）。

---

## 1. 表記の方針

### 1.1 日本語（基準）

- 説明の文は敬体（「〜します」「〜できます」）。画面の題・項目名・ボタンは体言止めか動詞の終止形（「Snapwell を有効にする」「保存」「既定に戻す」）。いまの画面に合わせる。
- 英数字の前後に半角スペースを入れる（「Snapwell を」「1 区画」「8 px」）。
- 「実機」は画面に使わない。いまの `OsHowDiagram.xaml.cs:80, 86`「実機で確認しました」は、フェーズ 2 で「確認しています」などに言い換える。

### 1.2 English

- **Sentence case**（文頭と固有名詞だけ大文字）。ページ名・項目名・ボタンもすべて（例: “Sync and backup”、“Reset to default”）。Windows 11 の設定アプリと同じ。
- 相手は “you”。短い命令形を使う。短縮形（“can't”、“don't”）を使ってよい。
- 完全な文には句点を付ける。題・項目名・ボタン・タグには付けない。日本語に句点がない行の説明も、文なら句点を付ける。
- 引用は “ ”（カーリークォート）。UI の要素名の引用は引用符でなく、そのままの表記でもよい（例: Turn on Zone preview）。日本語の「」の引用は “ ” にする。
- 範囲は en ダッシュ（1–64）。数字と単位の間は半角スペース（8 px、150 px/s）。
- 数を含む文は単数・複数を分ける（1 cell / 2 cells。`I18N.md` §7.2）。
- 長さ: 画面の幅で日本語の 1.4 倍以内を目安にする。固定幅の画面（ウィザード、トレイ、ホットキーの状態の列）は短い形を優先する。

### 1.3 简体中文

- 句読点は全角（，。：；！？（））。引用は “ ”。書名・UI 名の引用も “ ”。
- 中国語と英数字の間に半角スペースを入れる（例: “Snapwell 已启用”、“1 格”）。日本語の画面に合わせるため。
- 文体は丁寧で簡潔な書き言葉。“您” を使う（“你” は使わない）。命令は “请” を多用しない（エラーの対処など必要なところだけ）。
- 範囲は “1–64”。単位 px はそのまま（“8 px”）。

### 1.4 繁體中文（台灣）

- 句読点は全角（，。：；！？（））。引用は「」、入れ子は『』。
- 中国語と英数字の間に半角スペースを入れる（简体中文と同じ）。
- “您” を使う。台湾の Microsoft の語彙に合わせる（“按一下”、“資料夾”、“檔案”、“視窗”、“設定”、“預設”、“啟用／停用”）。
- 大陸の語彙を使わない（例: 软件→軟體、设置→設定、窗口→視窗、文件夹→資料夾、文件→檔案、屏幕→螢幕、默认→預設、单击→按一下、导出→匯出）。

### 1.5 キー・単位・記号（全言語共通）

| 種類 | 扱い |
|---|---|
| 修飾キー | Ctrl、Alt、Shift、Win（訳さない。順番は Ctrl・Alt・Shift・Win） |
| キー名 | Esc、Enter、Space、Tab、Backspace、Delete、Home、End、Insert、PageUp、PageDown、Pause、F1–F24（訳さない） |
| 矢印キーの記号 | ←、→、↑、↓（訳さない） |
| 文中の「矢印（キー）」 | arrow keys／方向键／方向鍵 |
| キーの組み合わせ | 「Ctrl+Alt+Space」（`+` の前後にスペースを入れない。`HotkeyCatalog.FormatChord`）。文中で「Win + 矢印」のように書いている箇所は、そのまま “Win + arrow keys”／“Win + 方向键”／“Win + 方向鍵” |
| 単位 | px、Hz、px/s（訳さない。数字との間に半角スペース） |
| 寸法 | 「8 × 8」（× の前後に半角スペース） |
| 分数 | 1/4、1/3、2/3（そのまま） |
| 物理の記号 | m、f、ζ、k、c（そのまま） |
| 区切りの「 · 」 | そのまま（ランチャーの説明行） |
| 省略記号 | 「…」（1 文字） |

---

## 2. 製品の語

| 日本語 | English | 简体中文 | 繁體中文（台灣） | 注記 |
|---|---|---|---|---|
| Snapwell | Snapwell | Snapwell | Snapwell | 訳さない |
| 区画 | cell | 格 | 格 | グリッドの 1 マス。数えるとき「2 区画」→ 2 cells／2 格／2 格。候補: en “zone”（FancyZones と紛れる）、zh “区域／區域”（領域と紛れる）、“单元格／儲存格”（表計算の語）。推奨は cell／格（「左へ 1 区画移動」→ “Move left 1 cell”／“向左移动 1 格”／“向左移動 1 格” が自然） |
| 区画のプレビュー | Zone preview | 目标区域预览 | 目標區域預覽 | 吸着先（複数の区画にまたがる）を塗る表示。「区画」を cell と訳しても、ここは “zone” のほうが通じる。統一するなら “Cell preview”／“格预览”／“格預覽” |
| 区画の線 | Cell lines | 格线 | 格線 | 「区画」に従う |
| グリッド | grid | 网格 | 網格 | 整列の方式名でも使う |
| 分割案 | grid | 网格方案 | 網格方案 | en は “grid layout” も候補。「分割案を選ぶ」→ “Choose a grid”／“选择网格方案”／“選擇網格方案” |
| モニター別の分割案 | Per-monitor grids | 按显示器的网格方案 | 依顯示器的網格方案 | |
| 分割と間隔（ページ名） | Grid and gaps | 网格与间距 | 網格與間距 | |
| 間隔（ウィンドウの間隔） | gap | 间距 | 間距 | 「ウィンドウの間隔」→ Window gap／窗口间距／視窗間距 |
| 画面端の余白 | Edge margin | 屏幕边距 | 螢幕邊距 | |
| 作業領域 | work area | 工作区 | 工作區 | |
| スナップ（Windows の機能） | Snap | 贴靠 | 貼齊 | **要確認**（OS の表記）。Snapwell 自身の動きには使わない |
| 標準のスナップ | Windows Snap | Windows 贴靠 | Windows 貼齊 | **要確認** |
| 吸着、吸着する（Snapwell の動き） | snap | 吸附 | 吸附 | en は Windows の Snap と同じ語になる。紛れる文では “Snapwell snapping” と書く |
| 吸着の方式 | Snap target | 吸附方式 | 吸附方式 | |
| 吸着先 | snap target | 吸附目标 | 吸附目標 | |
| 吸着半径 | Snap radius | 吸附半径 | 吸附半徑 | |
| 磁石 | magnet | 磁铁 | 磁鐵 | |
| 磁石の強さ | Magnet strength | 磁力强度 | 磁力強度 | |
| 投げる | throw | 甩出 | 甩出 | 候補: en “fling”、zh “抛出／拋出”、“甩动／甩動”。推奨は throw／甩出（タイトルバーをつかんで勢いよく離す動作） |
| 投げて配置 | Throw to place | 甩出放置 | 甩出放置 | ウィザード |
| なぞって配置 | Drag across cells | 拖选放置 | 拖曳選取放置 | ウィザード。ランチャーで区画をドラッグで範囲選択すること |
| キーで配置 | Place with keys | 用按键放置 | 用按鍵放置 | ウィザード |
| 予測の線 | Prediction line | 预测线 | 預測線 | |
| 物理モデル | physics model | 物理模型 | 物理模型 | |
| ばね | spring | 弹簧 | 彈簧 | |
| 操作感（ページ名） | Feel | 手感 | 手感 | feel（ライブラリ名）と同じ語 |
| プリセット | preset | 预设 | 預設 | |
| カスタム | Custom | 自定义 | 自訂 | |
| 質量 m | Mass m | 质量 m | 質量 m | |
| 固有振動数 f | Natural frequency f | 固有频率 f | 固有頻率 f | |
| 減衰比 ζ | Damping ratio ζ | 阻尼比 ζ | 阻尼比 ζ | |
| 導かれる値 | Derived values | 推导值 | 推導值 | |
| 動きを減らす | Reduce motion | 减少动画 | 減少動畫 | |
| 整列 | arrange | 排列 | 排列 | |
| 現在のモニターを整列 | Arrange this monitor | 排列当前显示器 | 排列目前的顯示器 | トレイ。短い形を優先 |
| 全モニターを整列 | Arrange all monitors | 排列所有显示器 | 排列所有顯示器 | |
| グリッド（整列の方式） | Grid | 网格 | 網格 | |
| 横一列 | Side by side | 横向并排 | 水平並排 | コードの名前は `ArrangeColumns`。候補: en “Columns” |
| 縦一列 | Stacked | 纵向堆叠 | 垂直堆疊 | コードは `ArrangeRows`。候補: en “Rows” |
| メインとスタック | Main and stack | 主窗口与堆叠 | 主視窗與堆疊 | |
| メイン（整列の先頭） | Main | 主窗口 | 主視窗 | ランチャーの行のタグ |
| スタック | Stack | 堆叠 | 堆疊 | |
| ウィンドウを選んで整列 | Arrange selected windows | 选择窗口并排列 | 選取視窗並排列 | ランチャー |
| 隣へ寄せる | Snap to neighbor | 贴靠相邻窗口 | 貼齊相鄰視窗 | zh の “贴靠／貼齊” は Windows の Snap と同じ語。候補: “靠向相邻窗口／靠向相鄰視窗” |
| 左の隣へ寄せる | Snap to left neighbor | 靠向左侧窗口 | 靠向左側視窗 | 上と合わせる |
| 広げる・縮める | Grow and shrink | 扩大与缩小 | 放大與縮小 | |
| 固定する位置 | Anchor | 固定点 | 固定點 | |
| 左上（固定する位置） | Top left | 左上 | 左上 | |
| 中心 | Center | 中心 | 中心 | |
| 近い辺 | Nearest edge | 最近的边 | 最近的邊 | |
| 中央に配置 | Center | 居中 | 置中 | |
| 中央に配置（サイズ維持） | Center (keep size) | 居中（保持大小） | 置中（保持大小） | |
| 最大化 | Maximize | 最大化 | 最大化 | |
| 左半分 | Left half | 左半边 | 左半邊 | |
| 左上の四分割 / 左上 1/4 | Top-left quarter | 左上 1/4 | 左上 1/4 | |
| 左 1/3、左 2/3、中央 1/3 | Left third, Left two-thirds, Center third | 左 1/3、左 2/3、中间 1/3 | 左 1/3、左 2/3、中間 1/3 | en はプリセット名だけ語で書く。表で長さを確かめる |
| 次のモニターへ | Next monitor | 移到下一个显示器 | 移到下一個顯示器 | |
| 一時停止・再開 | Pause or resume | 暂停／恢复 | 暫停／繼續 | |
| ランチャー | launcher | 启动器 | 啟動器 | アプリの起動ではなく配置用の画面。候補: en “Layout picker”、zh “布局面板／版面配置面板” |
| ランチャーを開く | Open launcher | 打开启动器 | 開啟啟動器 | 上に従う |
| オーバーレイ | overlay | 叠加层 | 覆疊層 | zh-Hant は “疊加層” も候補 |
| ウィンドウの角 | Window corners | 窗口圆角 | 視窗圓角 | |
| 角丸を強制する | Force rounded corners | 强制圆角 | 強制圓角 | |
| 小さい角丸 | Small rounded corners | 小圆角 | 小圓角 | |
| 角丸なし | Square corners | 直角 | 直角 | |
| Windows の既定 | Windows default | Windows 默认 | Windows 預設 | |
| 外観（ページ名） | Appearance | 外观 | 外觀 | |
| 標準機能（ページ名）／Windows の標準機能 | Windows features | Windows 功能 | Windows 功能 | 題は “Built-in Windows features”／“Windows 内置功能”／“Windows 內建功能” |
| アプリのルール（ページ名）／アプリごとのルール | App rules | 应用规则 | 應用程式規則 | |
| ルール | rule | 规则 | 規則 | |
| 条件 | condition | 条件 | 條件 | |
| 配置の扱い | Placement | 布局处理 | 配置處理 | |
| 全体設定に従う | Use global setting | 跟随全局设置 | 依照整體設定 | |
| 対象外 | Excluded | 排除 | 排除 | |
| 位置だけ | Position only | 仅位置 | 僅位置 | |
| 強制的に対象 | Always include | 强制包含 | 強制納入 | |
| サイズ固定のウィンドウ | fixed-size window | 固定大小的窗口 | 固定大小的視窗 | |
| ポップアップとフライアウト | pop-ups and flyouts | 弹出窗口和浮出窗口 | 快顯視窗和飛出視窗 | **要確認** |
| 同期 | sync | 同步 | 同步 | |
| 同期フォルダー | Sync folder | 同步文件夹 | 同步資料夾 | |
| 同期とバックアップ（ページ名） | Sync and backup | 同步与备份 | 同步與備份 | |
| 今すぐ同期 | Sync now | 立即同步 | 立即同步 | |
| バックアップ | backup | 备份 | 備份 | |
| 書き出す | Export | 导出 | 匯出 | |
| 取り込む | Import | 导入 | 匯入 | |
| 全体を置き換える | Replace all | 全部替换 | 全部取代 | |
| 選んだ項目だけ | Selected items only | 仅所选项目 | 僅選取的項目 | |
| 競合 | conflict | 冲突 | 衝突 | 同期 |
| この PC | this PC | 此电脑 | 這台電腦 | |
| 同期先 | sync folder copy | 同步目标 | 同步目標 | 「同期先の設定を採用」→ “Used the settings from the sync folder” |
| トレイ | tray | 托盘 | 系統匣 | Windows の正式名は notification area／通知区域／通知區域（**要確認**）。短さで推奨は左 |
| ホットキー | hotkey | 快捷键 | 快速鍵 | |
| 割り当て | assignment | 分配 | 指派 | 「割り当てを外す」→ Remove／移除分配／移除指派 |
| 未割り当て | Not assigned | 未分配 | 未指派 | |
| 重複 | Duplicate | 重复 | 重複 | タグ |
| ほかのアプリが使用中 | In use by another app | 已被其他应用占用 | 其他應用程式使用中 | en は状態の列 120 に収まらない見込み（`I18N.md` §8 の 10）。短い形 “In use” |
| 初回セットアップ | Setup | 初始设置 | 初始設定 | 窓のタイトル “Snapwell setup”／“Snapwell 初始设置”／“Snapwell 初始設定” |
| ようこそ | Welcome | 欢迎 | 歡迎 | |
| Snapwell の設定（窓のタイトル） | Snapwell settings | Snapwell 设置 | Snapwell 設定 | |
| 有効（Snapwell の状態） | Active | 已启用 | 已啟用 | トレイの状態タグ |
| 一時停止中 | Paused | 已暂停 | 已暫停 | |
| 異常 | Error | 异常 | 異常 | トレイの状態タグ |
| モニター構成: n 画面 | Monitors: {count} | 显示器：{count} 台 | 顯示器：{count} 台 | |

---

## 3. Windows の画面の名前（すべて要確認）

引用する文言は、各言語の Windows 11 の表記に合わせる。下の訳は仮。実装の前に、各言語の Windows（または Microsoft の用語集）で確かめる。

| 日本語の Windows | English | 简体中文 | 繁體中文 |
|---|---|---|---|
| ウィンドウのスナップ（設定アプリ） | Snap windows | 贴靠窗口 | 貼齊視窗 |
| スナップ レイアウト | Snap layouts | 贴靠布局 | 貼齊版面配置 |
| スナップ バー | Snap bar | 贴靠栏 | 貼齊列 |
| アニメーション効果 | Animation effects | 动画效果 | 動畫效果 |
| タスク スケジューラ | Task Scheduler | 任务计划程序 | 工作排程器 |
| スタートアップ | Startup | 启动 | 啟動 |
| スタート メニュー | Start menu | “开始”菜单 | [開始] 功能表 |
| タスクバー | taskbar | 任务栏 | 工作列 |
| 仮想デスクトップ | virtual desktop | 虚拟桌面 | 虛擬桌面 |
| エクスプローラー（Explorer） | File Explorer | 文件资源管理器 | 檔案總管 |
| 最大化ボタン | Maximize button | 最大化按钮 | 最大化按鈕 |
| ピクチャー イン ピクチャー | Picture-in-picture | 画中画 | 子母畫面 |
| 管理者 | administrator | 管理员 | 系統管理員 |
| サインイン | sign in | 登录 | 登入 |
| 通知領域 | notification area | 通知区域 | 通知區域 |
| UWP アプリ | UWP app | UWP 应用 | UWP 應用程式 |
| ウィンドウ クラス | window class | 窗口类 | 視窗類別 |
| プロセス名 | process name | 进程名称 | 處理程序名稱 |
| 正規表現 | regular expression | 正则表达式 | 規則運算式 |

---

## 4. UI の定型

| 日本語 | English | 简体中文 | 繁體中文（台灣） | 注記 |
|---|---|---|---|---|
| 保存 | Save | 保存 | 儲存 | |
| 保存して閉じる | Save and close | 保存并关闭 | 儲存並關閉 | |
| 保存せず閉じる | Close without saving | 不保存并关闭 | 不儲存並關閉 | |
| 取り消す（ダイアログ） | Cancel | 取消 | 取消 | |
| やめる（確認の取り消し） | Cancel | 取消 | 取消 | 日本語の使い分けは英中では区別しない |
| 追加 | Add | 添加 | 新增 | |
| 編集 | Edit | 编辑 | 編輯 | |
| 削除 | Delete | 删除 | 刪除 | |
| 変更 | Change | 更改 | 變更 | |
| 上へ／下へ | Move up／Move down | 上移／下移 | 上移／下移 | |
| 次へ／戻る／完了 | Next／Back／Done | 下一步／上一步／完成 | 下一步／上一步／完成 | |
| 前へ／次へ（ページ送り） | Previous／Next | 上一页／下一页 | 上一頁／下一頁 | ランチャー |
| 既定に戻す | Reset to default | 恢复默认值 | 還原為預設值 | |
| すべて既定に戻す | Reset all to default | 全部恢复默认值 | 全部還原為預設值 | |
| すべて選ぶ／すべて外す | Select all／Clear all | 全选／全部取消 | 全選／全部清除 | |
| 有効にする／無効にする | Turn on／Turn off | 启用／禁用 | 啟用／停用 | |
| 有効（列名、ルール） | On | 启用 | 啟用 | |
| オン／オフ | On／Off | 开／关 | 開啟／關閉 | 差分の値 |
| 準備中 | Coming soon | 即将推出 | 即將推出 | タグ |
| 未設定 | Not set | 未设置 | 未設定 | |
| 未選択 | None selected | 未选择 | 未選取 | |
| 選択中 | Selected | 已选择 | 已選取 | |
| 変更済み | Changed | 已更改 | 已變更 | ホットキーのタグ |
| 変更なし | No changes | 无更改 | 沒有變更 | 同期の状態 |
| 書き出し済み／取り込み済み | Exported／Imported | 已导出／已导入 | 已匯出／已匯入 | 同期の状態（Pushed／Pulled）。en は “Uploaded to sync folder” も候補 |
| 同期済み／まだ同期していません | Synced／Not synced yet | 已同步／尚未同步 | 已同步／尚未同步 | |
| 見送り: {理由} | Skipped: {reason} | 已跳过：{reason} | 已略過：{reason} | コロンは en 半角、zh 全角 |
| 失敗: {理由} | Failed: {reason} | 失败：{reason} | 失敗：{reason} | |
| 注意 | Note | 注意 | 注意 | |
| 詳細 | Details | 详细信息 | 詳細資料 | |
| 試す | Try it | 试一试 | 試試看 | 操作感のページ |
| 言語 | Language | 语言 | 語言 | |
| 自動（OS に合わせる） | Auto (match Windows) | 自动（跟随系统） | 自動（依照系統） | |
| 状態 | Status | 状态 | 狀態 | |
| 操作 | Action | 操作 | 動作 | ホットキーの列名 |
| キー | Key | 按键 | 按鍵 | |
| 〜件 | {count} item(s) | {count} 项 | {count} 項 | en は複数形を分ける（1 item／2 items） |
| 〜件を表示 | Showing {count} | 显示 {count} 项 | 顯示 {count} 項 | |
| 重複 {n} 件 | {count} duplicate(s) | {count} 处重复 | {count} 處重複 | |
| 〜のとき（説明の補足） | (when … is on) | （开启“…”时） | （開啟「…」時） | 例:（区画のプレビューがオンのとき）→ “(when Zone preview is on)” |
| 〜できませんでした。ファイルの場所とアクセス権を確認してください。 | Couldn't … Check the file location and permissions. | 无法…。请检查文件位置和访问权限。 | 無法…。請檢查檔案位置和存取權限。 | 失敗の定型 |
| 新しい版の設定のため、〜できません。Snapwell を更新してください。 | These settings are from a newer version, so … Update Snapwell. | 这些设置来自更新的版本，因此无法…。请更新 Snapwell。 | 這些設定來自較新的版本，因此無法…。請更新 Snapwell。 | 定型 |
| 保存すると、起動中の Snapwell に反映されます | Saving applies the changes to the running Snapwell | 保存后将应用到正在运行的 Snapwell | 儲存後會套用到執行中的 Snapwell | |
| ファイル | File | 文件 | 檔案 | |
| フォルダー | folder | 文件夹 | 資料夾 | |
| 設定 | settings | 设置 | 設定 | |
| ウィンドウ | window | 窗口 | 視窗 | |
| モニター | monitor | 显示器 | 顯示器 | |
| 画面（「画面端」「画面上端」） | screen | 屏幕 | 螢幕 | |
| ドラッグ | drag | 拖动 | 拖曳 | |
| クリック | click | 单击 | 按一下 | |
| アプリ | app | 应用 | 應用程式 | |
| ソフトウェア | software | 软件 | 軟體 | |
| 実機 | （使わない） | （不使用） | （不使用） | 画面の文言に出さない（§1.1） |

---

## 5. 言語名（各言語で同じ表記）

| 選択肢 | 表示（どの言語の画面でも） |
|---|---|
| 自動 | 画面の言語で訳す（§4 の「自動（OS に合わせる）」） |
| 日本語 | 日本語 |
| 英語 | English |
| 簡体字 | 简体中文 |
| 繁体字 | 繁體中文 |
