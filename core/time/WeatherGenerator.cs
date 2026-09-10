using System;
using System.IO;
using System.Text.Json;

namespace XingGame.Core.Time;

/// <summary>
/// 分季天气权重表（设计文档 §3.3）。放 JSON 而非硬编码，Mod 可以直接改数字（架构原则「数据驱动」）。
/// </summary>
public sealed class WeatherTable
{
    /// <summary>四季权重各自的合计，§3.3 规定为 100。</summary>
    public const int ExpectedTotal = 100;

    /// <summary>缺省权重表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/config/weather_probabilities.json";

    private static readonly int WeatherCount = Enum.GetValues<Weather>().Length;

    private readonly int[][] _weights;   // [季节][天气]，下标即枚举值
    private readonly int[] _totals;

    private WeatherTable(int[][] weights)
    {
        _weights = weights;
        _totals = new int[weights.Length];
        for (int season = 0; season < weights.Length; season++)
        {
            int total = 0;
            foreach (int weight in weights[season]) total += weight;
            _totals[season] = total;
        }
    }

    public int this[Season season, Weather weather] => _weights[(int)season][(int)weather];

    /// <summary>该季权重合计。掷点用它做上界，零权重天气因此永远不会被选中。</summary>
    public int Total(Season season) => _totals[(int)season];

    /// <summary>权重为 0 的天气在本季不可能出现。</summary>
    public bool CanOccur(Season season, Weather weather) => _weights[(int)season][(int)weather] > 0;

    public static WeatherTable FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        var weights = new int[GameTime.SeasonsPerYear][];
        for (int season = 0; season < weights.Length; season++)
            weights[season] = new int[WeatherCount];

        foreach (var seasonEntry in document.RootElement.EnumerateObject())
        {
            if (!Enum.TryParse<Season>(seasonEntry.Name, ignoreCase: true, out var season))
                throw new InvalidDataException($"权重表出现未知季节：{seasonEntry.Name}");

            foreach (var weatherEntry in seasonEntry.Value.EnumerateObject())
            {
                if (!Enum.TryParse<Weather>(weatherEntry.Name, ignoreCase: true, out var weather))
                    throw new InvalidDataException($"权重表出现未知天气：{weatherEntry.Name}");

                int weight = weatherEntry.Value.GetInt32();
                if (weight < 0)
                    throw new InvalidDataException($"{seasonEntry.Name}.{weatherEntry.Name} 权重为负：{weight}");

                weights[(int)season][(int)weather] = weight;
            }
        }

        // 合计不等于 100 会让掷点上界与设计文档的百分比脱钩，属于数据错误，启动即报
        for (int season = 0; season < weights.Length; season++)
        {
            int total = 0;
            foreach (int weight in weights[season]) total += weight;
            if (total != ExpectedTotal)
                throw new InvalidDataException($"{(Season)season} 权重合计为 {total}，应为 {ExpectedTotal}");
        }

        return new WeatherTable(weights);
    }

    public static WeatherTable FromFile(string path) => FromJson(File.ReadAllText(path));

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省权重表。不依赖 csproj 的复制规则——测试宿主与 Godot
    /// 的 BaseDirectory 都埋在各自的 bin 下，上溯才能同时命中工程根目录的 data/。
    /// </summary>
    public static WeatherTable LoadDefault()
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path);
    }

    private static string? FindDefaultFile()
    {
        foreach (string root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, DefaultRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// 按 <see cref="WeatherTable"/> 权重掷当日天气。
/// 确定性来自「参数哈希 → System.Random 定种构造」：不使用 <c>Random.Shared</c>、不读时钟，
/// 所以存档回放与测试都能复现同一天的天气。
/// </summary>
public sealed class WeatherGenerator : IWeatherGenerator
{
    private static readonly int WeatherCount = Enum.GetValues<Weather>().Length;

    public WeatherGenerator(WeatherTable table)
        => Table = table ?? throw new ArgumentNullException(nameof(table));

    public WeatherTable Table { get; }

    public Weather Roll(Season season, int dayIndex, int seed)
    {
        int total = Table.Total(season);
        int roll = new Random(Mix(seed, (int)season, dayIndex)).Next(total);

        int cumulative = 0;
        for (int weather = 0; weather < WeatherCount; weather++)
        {
            cumulative += Table[season, (Weather)weather];
            if (roll < cumulative) return (Weather)weather;
        }

        // 权重合计已由 WeatherTable 校验为 100，roll < total 恒成立；走到这里说明表被绕过
        throw new InvalidOperationException($"{season} 的权重表未覆盖 roll={roll}");
    }

    /// <summary>
    /// 把 (seed, season, dayIndex) 混成随机种子。末尾的雪崩步是必要的：System.Random 的定种
    /// 构造对相邻种子会产生相关的首个输出，直接用线性组合会让相邻两天的天气高度雷同。
    /// </summary>
    private static int Mix(int seed, int season, int dayIndex)
    {
        unchecked
        {
            int hash = seed;
            hash = (hash * 31) + season;
            hash = (hash * 31) + dayIndex;
            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= unchecked((int)0x846ca68b);
            hash ^= hash >> 16;
            return hash;
        }
    }
}
