namespace XingGame.Core.Save;

/// <summary>
/// 能把自己写进存档、从存档读回来的系统（ADR-009）。
/// </summary>
/// <remarks>
/// 版本归各系统自己管：<see cref="Version"/> 描述的是本条数据 JSON 的形态，
/// 与存档的基础设施版本（表结构）无关——某个系统改了格式，就自己 +1 并在
/// <see cref="Deserialize"/> 里兼容旧数据，不牵动全局版本号，也不逼其他系统跟着迁移。
/// </remarks>
public interface ISaveable
{
    /// <summary>键全局唯一，如 "time"。重复的键会让存进去的两份数据互相覆盖。</summary>
    string SaveKey { get; }

    /// <summary>本条数据自己的版本；JSON 形态变了就 +1。</summary>
    int Version { get; }

    string Serialize();

    /// <param name="fromVersion">存档里那条数据写下的 <see cref="Version"/>，用于兼容旧格式。</param>
    void Deserialize(string json, int fromVersion);
}
