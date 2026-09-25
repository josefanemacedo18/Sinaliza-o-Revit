using System.IO;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Registro simples de erros em %AppData%\SinalizaBIM\log.txt.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Error(string context, Exception ex) => Write($"ERRO [{context}] {ex}");

    public static void Info(string msg) => Write(msg);

    private static void Write(string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(PluginPaths.Root);
                var file = Path.Combine(PluginPaths.Root, "log.txt");
                if (File.Exists(file) && new FileInfo(file).Length > 2_000_000) File.Delete(file);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
            }
        }
        catch
        {
            // O log nunca deve interromper o Revit.
        }
    }
}

public static class PluginPaths
{
    public static string Root { get; } = InitRoot();

    /// <summary>%AppData%\SinalizaBIM – migra configurações e catálogo da versão anterior (SinalizacaoViaria).</summary>
    private static string InitRoot()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, "SinalizaBIM");
        var legacy = Path.Combine(appData, "SinalizacaoViaria");
        try
        {
            if (!Directory.Exists(root) && Directory.Exists(legacy))
            {
                Directory.CreateDirectory(root);
                foreach (var f in new[] { "config.json", "catalogo.json", "parametros-compartilhados.txt" })
                {
                    var src = Path.Combine(legacy, f);
                    if (File.Exists(src)) File.Copy(src, Path.Combine(root, f));
                }
            }
        }
        catch
        {
            // Migração é opcional.
        }
        return root;
    }
    public static string Settings => Path.Combine(Root, "config.json");
    public static string DefaultUserCatalog => Path.Combine(Root, "catalogo.json");
    public static string SharedParameters => Path.Combine(Root, "parametros-compartilhados.txt");
}
