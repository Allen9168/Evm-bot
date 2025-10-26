using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Soha.SwapFactory
{
    public static class SwapFactory
    {
        public static readonly Dictionary<string, ISwapFactory> SwapFactoryDictionary = new Dictionary<string, ISwapFactory>(StringComparer.OrdinalIgnoreCase);
        static SwapFactory()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            IEnumerable<Type> types = assembly.GetTypes().Where(it => typeof(BaseSwap).IsAssignableFrom(it))
                                    .Where(t => !t.IsAbstract && t.IsClass);
            foreach (Type type in types)
            {
                try
                {
                    object o = Activator.CreateInstance(type);
                    SwapFactoryDictionary.Add(((BaseSwap)o).swapAddress.ToLower(), (ISwapFactory)o);
                }
                catch (Exception)
                {
                    throw;
                }
            }
        }
        public static ISwapFactory SwapRouteFactory(string SwapRouterAddress)
        {
            SwapFactoryDictionary.TryGetValue(SwapRouterAddress, out ISwapFactory swapFactory);
            return swapFactory;
        }
    }
}