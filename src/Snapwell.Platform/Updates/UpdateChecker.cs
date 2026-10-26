using Snapwell.Core.Settings;
using Snapwell.Core.Updates;

namespace Snapwell.Platform.Updates;

/// <summary>本体と設定画面で共有する取得、検証、保存の処理。</summary>
public sealed class UpdateChecker(IUpdateFetcher fetcher, ISettingsStore<DeviceState> store,
    string? currentVersion, string? publicKey, UpdateOsArchitecture architecture, bool isPackaged = false)
{
    /// <summary>検証済みの確認だけを記録し、通知は確認時刻の保存前に行う。</summary>
    public async Task<UpdateCheckResponse> CheckAsync(CancellationToken cancellationToken,
        Func<DateTimeOffset>? utcNow = null, Action<UpdateOffer>? notify = null)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseVersion.TryParse(currentVersion, out var current);
            if (UpdateOfferResolver.UnavailableOutcome(current, publicKey, isPackaged) is { } unavailable)
            {
                return new(unavailable, Reason: unavailable == UpdateCheckOutcome.Failed
                    ? "Packaged applications do not check for updates." : null);
            }
            var fetched = await fetcher.FetchAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!fetched.IsSuccess) { return FetchFailed(fetched); }
            // 署名を検証するまでは内容を解釈しない。
            if (!ManifestSignature.Verify(fetched.Manifest!, fetched.Signature!, publicKey!))
            {
                // 公開の切り替わりによる組のずれを考慮し、両方を一回だけ取り直す。
                fetched = await fetcher.FetchAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!fetched.IsSuccess) { return FetchFailed(fetched); }
                if (!ManifestSignature.Verify(fetched.Manifest!, fetched.Signature!, publicKey!))
                {
                    return Failed("Manifest signature verification failed.");
                }
            }
            var parsed = UpdateManifestParser.Parse(fetched.Manifest!);
            if (parsed.Manifest is not { } manifest) { return Failed(parsed.Error ?? "Invalid manifest."); }
            // 対応する配布がないときは、案内用のキャッシュも最高受理版も変更しない。
            var asset = UpdateAssetSelector.Select(architecture, manifest);
            if (asset is null) { return Failed("No update asset matches the OS architecture."); }
            cancellationToken.ThrowIfCancellationRequested();
            var replay = false;
            var state = DeviceStateChanges.Update(store, latest =>
            {
                ReleaseVersion.TryParse(latest.HighestSeenVersion, out var highest);
                replay = !UpdateReplayGuard.Accept(manifest, highest);
                return replay ? latest : latest with
                {
                    HighestSeenVersion = manifest.Version.ToString(),
                    CachedUpdateManifest = fetched.Manifest,
                    CachedUpdateSignature = fetched.Signature,
                };
            });
            if (replay) { return Failed("Manifest replay was rejected."); }
            ReleaseVersion.TryParse(state.SkippedVersion, out var skipped);
            var evaluation = UpdateCheckPolicy.Evaluate(current!, manifest, skipped);
            UpdateOffer? offer = evaluation.Status == UpdateCheckStatus.Available
                ? new(manifest, asset, evaluation.RequiresUpdate) : null;
            cancellationToken.ThrowIfCancellationRequested();
            if (offer is not null) { notify?.Invoke(offer); }
            cancellationToken.ThrowIfCancellationRequested();
            string? diagnostic = null;
            try
            {
                DeviceStateChanges.Update(store, latest => latest with
                {
                    LastUpdateCheckUtc = (utcNow ?? (() => DateTimeOffset.UtcNow))(),
                });
            }
            catch (Exception exception)
            {
                // 案内と最高受理版は保存済みなので、日時だけの保存失敗で結果を失わせない。
                diagnostic = "Last update check time could not be saved: " + exception.Message;
            }
            return new(evaluation.Status switch
            {
                UpdateCheckStatus.Available => UpdateCheckOutcome.Available,
                UpdateCheckStatus.Skipped => UpdateCheckOutcome.Skipped,
                _ => UpdateCheckOutcome.UpToDate,
            }, offer, diagnostic);
        }
        catch (OperationCanceledException) { return Failed("Update check was cancelled."); }
        catch (Exception exception) { return Failed("Update check failed: " + exception.Message, retryable: true); }
    }

    private static UpdateCheckResponse FetchFailed(UpdateFetchResult result) =>
        Failed(result.Diagnostic ?? "Update fetch failed.", result.IsTransient);

    private static UpdateCheckResponse Failed(string reason, bool retryable = false) =>
        new(UpdateCheckOutcome.Failed, Reason: reason, Retryable: retryable);
}
