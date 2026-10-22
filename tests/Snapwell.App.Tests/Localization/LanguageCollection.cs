namespace Snapwell.App.Tests.Localization;

/// <summary>プロセス共通の言語を変えるテストを、既存の日本語のテストと同時に実行しない。</summary>
[CollectionDefinition("UI language", DisableParallelization = true)]
public sealed class LanguageCollection;
