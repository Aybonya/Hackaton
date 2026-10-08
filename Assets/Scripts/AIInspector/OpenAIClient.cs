using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Клиент взаимодействия с OpenAI API (chat/completions) через UnityWebRequest.
/// Поддерживает автоматическое сохранение ключа, обнаружение фиктивных ключей,
/// обработку сетевых ошибок и безопасный fallback.
/// </summary>
public static class OpenAIClient
{
    public const string DefaultApiKey = "";
    public const string PrefKey = "ALLUR_AI_OPENAI_KEY";
    public const string DefaultModel = "gpt-4o-mini";
    private const string ApiEndpoint = "https://api.openai.com/v1/chat/completions";

    [Serializable]
    public class ChatMessage
    {
        public string role;
        public string content;

        public ChatMessage() { }
        public ChatMessage(string r, string c)
        {
            role = r;
            content = c;
        }
    }

    [Serializable]
    public class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public float temperature = 0.5f;
        public int max_tokens = 2000;
    }

    [Serializable]
    public class ChoiceMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class Choice
    {
        public int index;
        public ChoiceMessage message;
        public string finish_reason;
    }

    [Serializable]
    public class ChatResponse
    {
        public string id;
        public Choice[] choices;
    }

    [Serializable]
    public class ErrorDetail
    {
        public string message;
        public string type;
        public string code;
    }

    [Serializable]
    public class ErrorResponse
    {
        public ErrorDetail error;
    }

    /// <summary>
    /// Проверяет, является ли токен тестовой заглушкой или пустым значением.
    /// </summary>
    public static bool IsPlaceholderKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return true;
        string t = key.Trim();
        if (t.Length < 12) return true;
        if (t.StartsWith("sk-dummy", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("sk-fake", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("sk-test", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("replace-me") ||
            t.Contains("your-key") ||
            t.Contains("placeholder"))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Загружает сохраненный API-ключ из PlayerPrefs (или возвращает дефолтный).
    /// </summary>
    public static string LoadApiKey(string fallback = null)
    {
        // 1. Проверяем локальный файл (который не коммитится в Git)
        try
        {
            if (System.IO.File.Exists("local_api_key.txt"))
            {
                string fKey = System.IO.File.ReadAllText("local_api_key.txt").Trim();
                if (!string.IsNullOrEmpty(fKey) && !IsPlaceholderKey(fKey))
                {
                    SaveApiKey(fKey);
                    return fKey;
                }
            }
        }
        catch { }

        // 2. Проверяем PlayerPrefs
        if (PlayerPrefs.HasKey(PrefKey))
        {
            string saved = PlayerPrefs.GetString(PrefKey, "").Trim();
            if (!string.IsNullOrEmpty(saved) && !IsPlaceholderKey(saved) && !saved.Contains("E0X6Qr"))
            {
                return saved;
            }
        }

        string defaultKey = string.IsNullOrEmpty(fallback) ? DefaultApiKey : fallback;
        if (!string.IsNullOrEmpty(defaultKey) && !IsPlaceholderKey(defaultKey))
        {
            SaveApiKey(defaultKey);
        }
        return defaultKey;
    }

    /// <summary>
    /// Сохраняет API-ключ в постоянное хранилище PlayerPrefs.
    /// </summary>
    public static void SaveApiKey(string key)
    {
        if (key != null)
        {
            PlayerPrefs.SetString(PrefKey, key.Trim());
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Выполняет корутину отправки запроса в OpenAI API.
    /// </summary>
    public static IEnumerator SendChatRequest(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        int timeoutSeconds,
        Action<string> onSuccess,
        Action<string> onError)
    {
        if (IsPlaceholderKey(apiKey))
        {
            onError?.Invoke("KEY_PLACEHOLDER: Указан тестовый/демо ключ. Переключение на встроенный движок симуляции.");
            yield break;
        }

        ChatRequest payload = new ChatRequest
        {
            model = string.IsNullOrEmpty(model) ? DefaultModel : model,
            temperature = 0.5f,
            max_tokens = 1200,
            messages = new ChatMessage[]
            {
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt)
            }
        };

        string jsonPayload = JsonUtility.ToJson(payload);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest req = new UnityWebRequest(ApiEndpoint, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
            req.timeout = timeoutSeconds > 0 ? timeoutSeconds : 15;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string resJson = req.downloadHandler.text;
                try
                {
                    ChatResponse resp = JsonUtility.FromJson<ChatResponse>(resJson);
                    if (resp != null && resp.choices != null && resp.choices.Length > 0 && resp.choices[0].message != null)
                    {
                        onSuccess?.Invoke(resp.choices[0].message.content);
                    }
                    else
                    {
                        onError?.Invoke("Пустой ответ от модели OpenAI.");
                    }
                }
                catch (Exception ex)
                {
                    onError?.Invoke("Ошибка разбора JSON ответа OpenAI: " + ex.Message);
                }
            }
            else
            {
                string errorText = req.error;
                string responseBody = req.downloadHandler != null ? req.downloadHandler.text : "";

                // Проверка детального ответа OpenAI об ошибке (например 401, 429 quota)
                if (!string.IsNullOrEmpty(responseBody))
                {
                    try
                    {
                        ErrorResponse errResp = JsonUtility.FromJson<ErrorResponse>(responseBody);
                        if (errResp != null && errResp.error != null && !string.IsNullOrEmpty(errResp.error.message))
                        {
                            if (errResp.error.code == "credit_balance_exhausted" || errResp.error.type == "insufficient_quota")
                            {
                                errorText = "Баланс аккаунта OpenAI исчерпан (credits exhausted). Для прямых запросов пополните баланс на platform.openai.com. Задействован встроенный цифровой эксперт Allur.";
                            }
                            else
                            {
                                errorText = errResp.error.message;
                            }
                        }
                    }
                    catch { }
                }

                onError?.Invoke(errorText);
            }
        }
    }
}
