using EasyStock.Server.Handlers.Images;

namespace EasyStock.Server.Helpers;

public static class ImagesHelper
{
    public static IServiceCollection AddImages(this IServiceCollection services) =>
        services.AddSingleton<ImageHandler>();
}
