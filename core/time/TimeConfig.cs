using System;
using System.IO;
using System.Text.Json;

namespace XingGame.Core.Time;

/// <summary>
/// 时间系统的可调参数。设计文档未定义现实时间映射，故这里不写死常量——
/// 保留 init 注入与 JSON 两条入口（ARCHITECTURE.md「未定义项备案」#1 待用户裁决）。
/// </summary>
public sealed record TimeConfig
{
    public int MinutesPerRealSecond { get; init; } = 10;

    /// <summary>从 <c>{ "minutesPerRealSecond": 10 }</c> 解析；缺键取默认值。</summary>
    public static TimeConfig FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        int minutesPerRealSecond = new TimeConfig().MinutesPerRealSecond;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, nameof(MinutesPerRealSecond), StringComparison.OrdinalIgnoreCase))
                minutesPerRealSecond = property.Value.GetInt32();
        }

        // 非正流速会让时间永久停滞，配置写错要在启动时就炸掉，而不是玩到一半才发现
        if (minutesPerRealSecond <= 0)
            throw new InvalidDataException($"minutesPerRealSecond 必须为正数，实际为 {minutesPerRealSecond}");

        return new TimeConfig { MinutesPerRealSecond = minutesPerRealSecond };
    }

    public static TimeConfig FromFile(string path) => FromJson(File.ReadAllText(path));
}
