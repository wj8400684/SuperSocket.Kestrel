using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Core;

public static class TaskExtension
{
    /// <summary>
    /// 安全字典
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<Task, object>> Cache = new();

    /// <summary>
    /// 转换为Task(T)类型
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="task">任务</param>
    /// <returns></returns>
    public static Task<T> ToTask<T>(this Task task)
    {
        return task.ToTask<T>(task.GetType());
    }

    /// <summary>
    /// 转换为TaskOf(T)类型
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="task">任务</param>
    /// <param name="taskType">任务类型</param>
    /// <returns></returns>
    public static async Task<T> ToTask<T>(this Task task, Type taskType)
    {
        await task;

        return (T)Cache
            .GetOrAdd(taskType, CreateResultInvoker)
            .Invoke(task);
    }

    /// <summary>
    /// 创建Task类型获取Result的委托
    /// </summary>
    /// <param name="taskType">Task实例的类型</param>
    /// <returns></returns>
    private static Func<Task, object> CreateResultInvoker(Type taskType)
    {
        const string propertyName = "Result";

        if (!taskType.IsGenericType) // || taskType.GetGenericTypeDefinition() != typeof(Task<>)
            throw new ArgumentException("返回类型不是范型 task<T>");

        var arg = Expression.Parameter(typeof(Task));
        var castArg = Expression.Convert(arg, taskType);
        var fieldAccess = Expression.Property(castArg, propertyName);
        var castResult = Expression.Convert(fieldAccess, typeof(object));
        var lambda = Expression.Lambda<Func<Task, object>>(castResult, arg);

        return lambda.Compile();
    }
}