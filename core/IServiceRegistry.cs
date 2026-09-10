namespace XingGame.Core;

/// <summary>服务注册表：系统之间按接口取用，避免节点硬引用具体实现。</summary>
public interface IServiceRegistry
{
    void Register<T>(T service) where T : class;

    /// <summary>未注册时抛 <see cref="System.InvalidOperationException"/>。</summary>
    T Get<T>() where T : class;

    bool TryGet<T>(out T service) where T : class;
}
