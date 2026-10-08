using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Polls the equipment API and exposes its state on a scene object.
/// This script must remain outside an Editor folder because it runs in a player build.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public sealed class FactoryMonitor : MonoBehaviour
{
#pragma warning disable CS0649
    [Serializable]
    private sealed class MachineData { public string machine_id; public string status; }
#pragma warning restore CS0649

    public enum MachineStatus { Unknown, Running, Idle, Error, Offline }

    [Header("Equipment")]
    [SerializeField] private string machineId = "Machine_1";
    [SerializeField] private string apiBaseUrl = "http://127.0.0.1:8000/api/equipment";
    [SerializeField, Min(0.1f)] private float pollingIntervalSeconds = 2f;
    [SerializeField, Min(1)] private int requestTimeoutSeconds = 5;

    [Header("Status colors")]
    [SerializeField] private Color runningColor = Color.green;
    [SerializeField] private Color idleColor = Color.yellow;
    [SerializeField] private Color errorColor = Color.red;
    [SerializeField] private Color offlineColor = new Color(0.45f, 0.45f, 0.45f);
    [SerializeField] private Color unknownColor = Color.white;

    public string MachineId => machineId;
    public MachineStatus Status { get; private set; } = MachineStatus.Unknown;
    public event Action<FactoryMonitor, MachineStatus> StatusChanged;

    private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
    private Renderer machineRenderer;
    private Coroutine pollingRoutine;
    private string lastError;

    private void Awake()
    {
        machineRenderer = GetComponent<Renderer>();
        ApplyStatus(MachineStatus.Unknown);
    }

    private void OnEnable() => pollingRoutine = StartCoroutine(PollEquipment());

    private void OnDisable()
    {
        if (pollingRoutine == null) return;
        StopCoroutine(pollingRoutine);
        pollingRoutine = null;
    }

    /// <summary>Called by the builder to keep the scene configuration explicit.</summary>
    public void Configure(string id, string baseUrl = null)
    {
        machineId = id;
        if (!string.IsNullOrWhiteSpace(baseUrl)) apiBaseUrl = baseUrl;
    }

    private IEnumerator PollEquipment()
    {
        var delay = new WaitForSeconds(pollingIntervalSeconds);
        while (isActiveAndEnabled)
        {
            if (string.IsNullOrWhiteSpace(machineId) || string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                ReportFailure("Machine ID or API URL is not configured.");
            }
            else
            {
                using (var request = UnityWebRequest.Get(BuildEquipmentUrl()))
                {
                    request.timeout = requestTimeoutSeconds;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success) HandleResponse(request.downloadHandler.text);
                    else ReportFailure($"{request.error} ({request.responseCode})");
                }
            }
            yield return delay;
        }
    }

    private string BuildEquipmentUrl() => $"{apiBaseUrl.TrimEnd('/')}/{UnityWebRequest.EscapeURL(machineId)}";

    private void HandleResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) { ReportFailure("The API returned an empty response."); return; }
        MachineData data;
        try
        {
            data = JsonUtility.FromJson<MachineData>(json);
        }
        catch (ArgumentException)
        {
            ReportFailure("The API returned invalid JSON.");
            return;
        }
        if (data == null || string.IsNullOrWhiteSpace(data.status)) { ReportFailure("The API response does not contain a status."); return; }
        lastError = null;
        ApplyStatus(ParseStatus(data.status));
    }

    private void ReportFailure(string error)
    {
        ApplyStatus(MachineStatus.Offline);
        if (lastError == error) return;
        lastError = error;
        Debug.LogWarning($"[{name} / {machineId}] Equipment polling failed: {error}", this);
    }

    private static MachineStatus ParseStatus(string value)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "running": return MachineStatus.Running;
            case "idle": return MachineStatus.Idle;
            case "error": return MachineStatus.Error;
            case "offline": return MachineStatus.Offline;
            default: return MachineStatus.Unknown;
        }
    }

    private void ApplyStatus(MachineStatus newStatus)
    {
        var changed = Status != newStatus;
        Status = newStatus;
        if (machineRenderer != null)
        {
            var color = GetColor(newStatus);
            machineRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            machineRenderer.SetPropertyBlock(propertyBlock);
        }
        if (changed) StatusChanged?.Invoke(this, newStatus);
    }

    private Color GetColor(MachineStatus status)
    {
        switch (status)
        {
            case MachineStatus.Running: return runningColor;
            case MachineStatus.Idle: return idleColor;
            case MachineStatus.Error: return errorColor;
            case MachineStatus.Offline: return offlineColor;
            default: return unknownColor;
        }
    }
}
