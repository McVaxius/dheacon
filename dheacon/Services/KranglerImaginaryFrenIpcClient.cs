using System.Text;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed class KranglerImaginaryFrenIpcClient : IDisposable
{
    private const string SetFromJsonName = "Krangler.ImaginaryFren.SetFromJson";
    private const string GetStatusJsonName = "Krangler.ImaginaryFren.GetStatusJson";
    private const string ExportPresetJsonName = "Krangler.Presets.ExportPresetJson";
    private const string ImportPresetJsonName = "Krangler.Presets.ImportPresetJson";
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
    };

    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly DheaconPresetService presetService;
    private readonly ICallGateSubscriber<string, string> setSubscriber;
    private readonly ICallGateSubscriber<string> statusSubscriber;
    private readonly ICallGateSubscriber<string, string> exportPresetSubscriber;
    private readonly ICallGateSubscriber<string, string> importPresetSubscriber;
    private DateTime nextReconcileUtc = DateTime.MinValue;

    public KranglerImaginaryFrenIpcClient(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        Configuration configuration,
        DheaconPresetService presetService)
    {
        this.log = log;
        this.configuration = configuration;
        this.presetService = presetService;
        setSubscriber = pluginInterface.GetIpcSubscriber<string, string>(SetFromJsonName);
        statusSubscriber = pluginInterface.GetIpcSubscriber<string>(GetStatusJsonName);
        exportPresetSubscriber = pluginInterface.GetIpcSubscriber<string, string>(ExportPresetJsonName);
        importPresetSubscriber = pluginInterface.GetIpcSubscriber<string, string>(ImportPresetJsonName);
    }

    public string LastStatus { get; private set; } = "Krangler follower IPC idle.";
    public string LastError { get; private set; } = string.Empty;
    public bool LastRequestEnabled { get; private set; }
    public bool LastResponseSpawned { get; private set; }

    public void Update()
    {
        var now = DateTime.UtcNow;
        if (now < nextReconcileUtc)
            return;

        nextReconcileUtc = now + ReconcileInterval;
        Reconcile();
    }

    public void ReconcileNow()
    {
        nextReconcileUtc = DateTime.UtcNow + ReconcileInterval;
        Reconcile();
    }

    public void Dispose()
    {
        try
        {
            SendDesired(enabled: false, name: "Golden Sven", presetKey: "e97d1e17-9247-46aa-a9ad-b942ab905d31");
        }
        catch
        {
            // Soft cleanup only; Krangler also despawns its own actor on unload.
        }
    }

    public string? TryExportPresetBase64(string presetKey)
    {
        try
        {
            var resultJson = exportPresetSubscriber.InvokeFunc(presetKey);
            using var document = JsonDocument.Parse(resultJson);
            if (!ReadOk(document.RootElement))
                return null;

            if (!document.RootElement.TryGetProperty("exportJson", out var exportJsonElement) ||
                exportJsonElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var exportJson = exportJsonElement.GetString();
            return string.IsNullOrWhiteSpace(exportJson)
                ? null
                : Convert.ToBase64String(Encoding.UTF8.GetBytes(exportJson));
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastStatus = "Krangler preset export unavailable.";
            return null;
        }
    }

    public bool TryImportPresetBase64(string encoded, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(encoded))
        {
            message = "No embedded Krangler preset was present.";
            return false;
        }

        try
        {
            var exportJson = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var response = importPresetSubscriber.InvokeFunc(exportJson);
            using var document = JsonDocument.Parse(response);
            if (ReadOk(document.RootElement))
            {
                message = ReadString(document.RootElement, "status", "Imported embedded Krangler preset.");
                return true;
            }

            message = ReadString(document.RootElement, "error", "Krangler rejected embedded preset import.");
            return false;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            LastError = ex.Message;
            LastStatus = "Krangler preset import unavailable.";
            return false;
        }
    }

    private void Reconcile()
    {
        var activePreset = presetService.ActivePreset;
        var desiredFren = activePreset.ImaginaryFren;
        var shouldEnable = configuration.PluginEnabled &&
                           string.Equals(activePreset.Id, DheaconPresetIds.ReadingRoegadyn, StringComparison.OrdinalIgnoreCase) &&
                           desiredFren?.Enabled == true;

        var name = desiredFren?.Name ?? "Golden Sven";
        var presetKey = desiredFren?.PresetKey ?? "e97d1e17-9247-46aa-a9ad-b942ab905d31";
        SendDesired(shouldEnable, name, presetKey);
    }

    private void SendDesired(bool enabled, string name, string presetKey)
    {
        LastRequestEnabled = enabled;
        var requestJson = JsonSerializer.Serialize(new
        {
            enabled,
            name,
            presetKey,
            persist = false,
            source = "dheacon",
        }, JsonOptions);

        try
        {
            var responseJson = setSubscriber.InvokeFunc(requestJson);
            ObserveStatusResponse(responseJson);
        }
        catch (Exception ex)
        {
            LastResponseSpawned = false;
            LastError = ex.Message;
            LastStatus = enabled
                ? "Krangler Imaginary Fren IPC unavailable; speech continues without follower."
                : "Krangler Imaginary Fren IPC unavailable while disabling follower.";
            log.Debug(ex, "[Dheacon] Krangler Imaginary Fren IPC soft failure.");
        }

        try
        {
            var statusJson = statusSubscriber.InvokeFunc();
            ObserveStatusResponse(statusJson);
        }
        catch
        {
            // SetFromJson already reports the useful soft failure.
        }
    }

    private void ObserveStatusResponse(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        LastResponseSpawned = root.TryGetProperty("spawned", out var spawnedElement) &&
                              spawnedElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                              spawnedElement.GetBoolean();
        LastStatus = ReadString(root, "status", "Krangler follower status received.");
        LastError = ReadString(root, "error", string.Empty);
    }

    private static bool ReadOk(JsonElement root)
        => root.TryGetProperty("ok", out var okElement) &&
           okElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           okElement.GetBoolean();

    private static string ReadString(JsonElement root, string propertyName, string fallback)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
}
