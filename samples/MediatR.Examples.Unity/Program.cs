using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MediatR.Pipeline;
using Unity;
using Unity.Lifetime;
using Unity.Registration;

namespace MediatR.Examples.Unity
{
    internal class Program
    {
        private static Task Main(string[] args)
        {
            var writer = new WrappingWriter(Console.Out);
            var mediator = BuildMediator(writer);

            return Runner.Run(mediator, writer, "Unity");
        }

        private static IMediator BuildMediator(WrappingWriter writer)
        {
            var container = new UnityContainer();

            container.RegisterInstance<TextWriter>(writer)
                     .RegisterMediator(new HierarchicalLifetimeManager())
                     .RegisterMediatorHandlers(Assembly.GetAssembly(typeof(Ping)));

            container.RegisterType(typeof(IPipelineBehavior<,>), typeof(RequestPreProcessorBehavior<,>), "RequestPreProcessorBehavior");
            container.RegisterType(typeof(IPipelineBehavior<,>), typeof(RequestPostProcessorBehavior<,>), "RequestPostProcessorBehavior");
            container.RegisterType(typeof(IPipelineBehavior<,>), typeof(GenericPipelineBehavior<,>), "GenericPipelineBehavior");
            container.RegisterType(typeof(IRequestPreProcessor<>), typeof(GenericRequestPreProcessor<>), "GenericRequestPreProcessor");
            container.RegisterType(typeof(IRequestPostProcessor<,>), typeof(GenericRequestPostProcessor<,>), "GenericRequestPostProcessor");
            container.RegisterType(typeof(IRequestPostProcessor<,>), typeof(ConstrainedRequestPostProcessor<,>), "ConstrainedRequestPostProcessor");

            // Unity doesn't support generic constraints
            //container.RegisterType(typeof(INotificationHandler<>), typeof(ConstrainedPingedHandler<>), "ConstrainedPingedHandler");

            return container.Resolve<IMediator>();
        }
    }

    // ReSharper disable once InconsistentNaming
    public static class IUnityContainerExtensions
    {
        public static IUnityContainer RegisterMediator(this IUnityContainer container, LifetimeManager lifetimeManager)
        {
            return container.RegisterType<IMediator, Mediator>(lifetimeManager)
                .RegisterInstance<SingleInstanceFactory>(t => container.IsRegistered(t) ? container.Resolve(t) : null)
                .RegisterInstance<MultiInstanceFactory>(t =>
                {
                    var allHandlers = container.ResolveAll(t).ToList();
                    if (t.IsGenericTypeOf(typeof(INotificationHandler<>)))
                    {
                        allHandlers.AddGenericTypes(container, typeof(INotificationHandler<INotification>));
                    }

                    return allHandlers;
                });
        }

        public static IUnityContainer RegisterMediatorHandlers(this IUnityContainer container, Assembly assembly)
        {
            return container.RegisterTypesImplementingType(assembly, typeof(IRequestHandler<>))
                            .RegisterTypesImplementingType(assembly, typeof(IRequestHandler<,>))
                            .RegisterNamedTypesImplementingType(assembly, typeof(INotificationHandler<>));
        }

        internal static bool IsGenericTypeOf(this Type type, Type genericType)
        {
            return type.IsGenericType &&
                   type.GetGenericTypeDefinition() == genericType;
        }

        internal static void AddGenericTypes(this List<object> list, IUnityContainer container, Type genericType)
        {
            var genericHandlerRegistrations =
                container.Registrations.Where(reg => reg.RegisteredType == genericType);

            foreach (var handlerRegistration in genericHandlerRegistrations)
            {
                if (list.All(item => item.GetType() != handlerRegistration.MappedToType))
                {
                    list.Add(container.Resolve(handlerRegistration.MappedToType));
                }
            }
        }

        /// <summary>
        ///     Register all implementations of a given type for provided assembly.
        /// </summary>
        public static IUnityContainer RegisterTypesImplementingType(this IUnityContainer container, Assembly assembly, Type type)
        {
            foreach (var implementation in assembly.GetTypes().Where(t => t.GetInterfaces().Any(implementation => IsSubclassOfRawGeneric(type, implementation))))
            {
                var interfaces = implementation.GetInterfaces();
                foreach (var @interface in interfaces)
                    container.RegisterType(@interface, implementation);
            }

            return container;
        }

        /// <summary>
        ///     Register all implementations of a given type for provided assembly.
        /// </summary>
        public static IUnityContainer RegisterNamedTypesImplementingType(this IUnityContainer container, Assembly assembly, Type type)
        {
            foreach (var implementation in assembly.GetTypes().Where(t => t.GetInterfaces().Any(implementation => IsSubclassOfRawGeneric(type, implementation))))
            {
                var interfaces = implementation.GetInterfaces();
                foreach (var @interface in interfaces)
                    container.RegisterType(@interface, implementation, implementation.FullName);
            }

            return container;
        }

        private static bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                var currentType = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == currentType)
                    return true;

                toCheck = toCheck.BaseType;
            }

            return false;
        }
    }
}