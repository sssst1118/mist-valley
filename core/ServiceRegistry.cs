using System;
using System.Collections.Generic;

namespace XingGame.Core;

/// <summary>
/// 服务注册表，键为<b>调用方声明的类型参数</b>而非运行时类型：<c>Register&lt;IClock&gt;(impl)</c> 要用
/// <c>Get&lt;IClock&gt;()</c> 取，用 <c>Get&lt;Clock&gt;()</c> 取不到 —— 按接口取用才是这个类的意义。
/// </summary>
/// <remarks>
/// 重复注册同一类型时<b>后注册者覆盖先注册者</b>：测试里换替身、Mod 覆盖原实现都靠这条，
/// 若改成抛异常，两者都要另开一套卸载机制。代价是写错两遍注册不会报错，故覆盖行为固定由测试守住。
/// </remarks>
public sealed class ServiceRegistry : IServiceRegistry
{
    private readonly Dictionary<Type, object> _services = new();

    public void Register<T>(T service) where T : class
    {
        ArgumentNullException.ThrowIfNull(service);

        _services[typeof(T)] = service;
    }

    public T Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service)) return (T)service;

        // 带上类型名：缺注册是装配期错误，报错信息要能直接指出漏了谁
        throw new InvalidOperationException($"服务未注册：{typeof(T).FullName}");
    }

    public bool TryGet<T>(out T service) where T : class
    {
        if (_services.TryGetValue(typeof(T), out var found))
        {
            service = (T)found;
            return true;
        }

        service = null!;   // out 必须先赋值：未注册以返回值 false 表达，输出用 null
        return false;
    }
}
