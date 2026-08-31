using System.Reflection;
using CleanArchitecture.Application.Common.Security;
using MediatR;

namespace CleanArchitecture.Application.UnitTests.Architecture;

internal static class ApplicationRequestInventory
{
    internal static IEnumerable<Type> GetConcreteRequests(Assembly assembly)
    {
        return assembly.GetTypes().Where(IsConcreteRequest);
    }

    internal static bool IsConcreteRequest(Type type)
    {
        return !type.IsInterface
            && !type.IsAbstract
            && !type.ContainsGenericParameters
            && typeof(IBaseRequest).IsAssignableFrom(type)
            && !typeof(INotification).IsAssignableFrom(type);
    }

    internal static bool IsPublicRequest(Type requestType)
    {
        return typeof(IPublicRequest).IsAssignableFrom(requestType);
    }
}
