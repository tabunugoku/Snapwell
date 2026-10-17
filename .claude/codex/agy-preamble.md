【agy で動かすときの決まり (プロンプトの前に付ける)】
- 最初にリポジトリの CLAUDE.md を読み、「守ること」と「コードとコメントの書き方」に従う。
- シェルで実行してよいコマンドは `dotnet build ...` だけ。ほかのコマンド (git、ls、dir、Get-ChildItem、cat、type、rg、findstr、dotnet test、dotnet format、dotnet restore など) は拒否され、その時点で実行全体が止まる。ファイルを読む・探す・一覧するのは、組み込みのファイルのツールで行い、シェルのコマンドは使わない。
- dotnet test と dotnet format は実行しない。checks の test と format は not_run にする (Opus がローカルで実行する)。
- ファイルを変えてよいのは、いまの作業フォルダ (worktree) の中の、プロンプトで許されたファイルだけ。
- 最後の応答は、指定された JSON スキーマの形で返す。

