namespace Snapwell.Core.Settings;

/// <summary>新しい版の設定を上書きしようとしたことを表す。</summary>
public sealed class FutureSettingsVersionException(string message) : InvalidOperationException(message);
