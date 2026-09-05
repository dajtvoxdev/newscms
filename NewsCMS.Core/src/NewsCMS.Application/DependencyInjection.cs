using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace NewsCMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // FluentValidation - auto register tất cả validator
        services.AddValidatorsFromAssembly(assembly);

        // Service contract -> implementation sẽ được đăng ký tại Infrastructure
        // (vì hầu hết service truy cập DB)
        return services;
    }
}
