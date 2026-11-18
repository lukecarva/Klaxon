using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Klaxon.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Klaxon.Core.Persistence;

/// <summary>Loads and saves the alarm list.</summary>
public interface IAlarmStore
{
    /// <summary>Reads all alarms.</summary>
    IReadOnlyList<Alarm> Load();

    /// <summary>Writes all alarms.</summary>
    void Save(IEnumerable<Alarm> alarms);
}

/// <summary>Stores alarms in a human-readable JSON file.</summary>
public sealed class JsonAlarmStore : IAlarmStore
{
    /// <summary>Serializer options: indented, no nulls, unescaped accents, enums as names.</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger<JsonAlarmStore> _log;

    public JsonAlarmStore(string path, ILogger<JsonAlarmStore>? log = null)
    {
        _path = path;
        _log = log ?? NullLogger<JsonAlarmStore>.Instance;
    }

    /// <summary>Reads the alarms; quarantines an unreadable file and returns empty.</summary>
    public IReadOnlyList<Alarm> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<Alarm>>(json, SerializerOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Move the unreadable file aside instead of overwriting it.
            var quarantine = $"{_path}.corrompido-{DateTime.Now:yyyyMMdd-HHmmss}";

            _log.LogError(
                ex,
                "alarms.json ilegível. Movendo para {Quarentena} e começando vazio.",
                quarantine);

            try
            {
                File.Move(_path, quarantine);
            }
            catch (IOException moveEx)
            {
                _log.LogError(moveEx, "Não deu para mover o arquivo corrompido.");
            }

            return [];
        }
    }

    /// <summary>Writes the alarms atomically (temp file + replace with backup).</summary>
    public void Save(IEnumerable<Alarm> alarms)
    {
        var list = alarms.ToList();
        var json = JsonSerializer.Serialize(list, SerializerOptions);

        var folder = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(_path))
        {
            File.Replace(temp, _path, _path + ".bak", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, _path);
        }

        _log.LogDebug("{Quantidade} alarme(s) salvos em {Caminho}.", list.Count, _path);
    }
}
