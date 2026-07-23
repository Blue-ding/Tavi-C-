using System;
using System.Linq;
using System.Reflection;

namespace Tavi.Application.LanguageModel
{
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = true)]
    public class UniqueInterfaceAttribute : Attribute
    {
        
    }

    public static class UniqueInterfaceUtility
    {
        public static T Instantiate<T>()
        {
            if (typeof(T).GetCustomAttributes().All(a => a.GetType() != typeof(UniqueInterfaceAttribute)))
                throw new Exception($"试图实例化一个非独一接口{typeof(T).FullName}！");
            var implementations = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t =>
                    typeof(T).IsAssignableFrom(t) &&
                    t.IsClass &&
                    !t.IsAbstract)
                .ToList();
            if (implementations.Count == 0) throw new Exception($"没有找到{typeof(T).FullName}的实现！");
            if (implementations.Count > 1)
            {
                string message = $"{typeof(T).FullName}存在多个实现！见:";
                foreach (var implementation in implementations)
                {
                    message += "\n" + implementation.FullName;
                }
                throw new Exception(message);
            }

            try
            {
                object instance = Activator.CreateInstance(implementations[0])
                                  ?? throw new InvalidOperationException("构造函数返回了空实例。");
                return (T)instance;
            }
            catch (Exception e)
            {
                throw new Exception($"{typeof(T).FullName}无法实例化，您是否忘记了无参构造函数？", e);
            }
        }
    }
}
