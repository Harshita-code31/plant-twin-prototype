using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public enum RpcStatus
{
    Ok,
    NetworkError,   // offline, timeout, DNS, 5xx — worth retrying later
    ServerRejected, // the function raised (e.g. invalid_credentials) — retrying won't help
}

public struct RpcResult
{
    public RpcStatus status;
    public string body;
    public string errorMessage;

    public bool Ok => status == RpcStatus.Ok;
}

/// <summary>Minimal PostgREST RPC caller on top of UnityWebRequest (no Supabase SDK).</summary>
public static class SupabaseRpc
{
    [Serializable]
    private class PostgrestError
    {
        public string message;
    }

    public static IEnumerator Call(string function, string jsonBody, Action<RpcResult> onDone)
    {
        var config = SupabaseConfig.Instance;
        if (!config.IsConfigured)
        {
            onDone(new RpcResult { status = RpcStatus.NetworkError, errorMessage = "not_configured" });
            yield break;
        }

        string url = config.projectUrl.TrimEnd('/') + "/rest/v1/rpc/" + function;
        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = config.requestTimeoutSeconds,
        };
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("apikey", config.anonKey);
        // Publishable keys identify the app via apikey; they are not user JWTs.
        // Preserve the legacy anon JWT header for older project configurations.
        if (!config.anonKey.StartsWith("sb_publishable_", StringComparison.Ordinal))
        {
            request.SetRequestHeader("Authorization", "Bearer " + config.anonKey);
        }

        yield return request.SendWebRequest();

        string body = request.downloadHandler?.text;
        if (request.result == UnityWebRequest.Result.Success)
        {
            onDone(new RpcResult { status = RpcStatus.Ok, body = body });
            yield break;
        }

        bool rejected = request.result == UnityWebRequest.Result.ProtocolError
                        && request.responseCode >= 400 && request.responseCode < 500;
        onDone(new RpcResult
        {
            status = rejected ? RpcStatus.ServerRejected : RpcStatus.NetworkError,
            body = body,
            errorMessage = rejected ? ParseErrorMessage(body) : request.error,
        });
    }

    private static string ParseErrorMessage(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "unknown_error";
        }
        try
        {
            return JsonUtility.FromJson<PostgrestError>(body)?.message ?? body;
        }
        catch (ArgumentException)
        {
            return body;
        }
    }
}
