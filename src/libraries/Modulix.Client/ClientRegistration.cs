using Microsoft.Extensions.DependencyInjection;

namespace Modulix.Client;

/// <summary>
/// Extension methods for registering the Modulix API client in the dependency injection container.
/// </summary>
public static class ClientRegistration
{
    /// <param name="services">The IServiceCollection to add the client to.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the HTTP client to the <see cref="IModulixApi"/> and sets the specified base address.
        /// </summary>
        /// <param name="apiBaseAddress">The API base address.</param>
        /// <returns><see cref="IHttpClientBuilder"/> for additional configuring of the underlying <c>HttpClient</c>.</returns>
        /// <exception cref="UriFormatException">Thrown when the provided API base address is not a valid URI.</exception>
        public IHttpClientBuilder AddModulixApi(string apiBaseAddress)
        {
            var baseAddress = new Uri(apiBaseAddress);
            return services.AddModulixApi(_ => baseAddress);
        }

        /// <summary>
        /// Adds the HTTP client to the <see cref="IModulixApi"/> and sets the specified base address.
        /// </summary>
        /// <param name="apiBaseAddress">The API base address.</param>
        /// <returns><see cref="IHttpClientBuilder"/> for additional configuring of the underlying <c>HttpClient</c>.</returns>
        public IHttpClientBuilder AddModulixApi(Uri apiBaseAddress)
            => services.AddModulixApi(_ => apiBaseAddress);

        /// <summary>
        /// Adds the HTTP client to the <see cref="IModulixApi"/> and sets the base address to the return of the specified <paramref name="apiBaseAddressFactory"/>.
        /// </summary>
        /// <param name="apiBaseAddressFactory">Factory method to evaluate the API base address from the passed <see cref="IServiceProvider"/>.</param>
        /// <returns><see cref="IHttpClientBuilder"/> for additional configuring of the underlying <c>HttpClient</c>.</returns>
        public IHttpClientBuilder AddModulixApi(Func<IServiceProvider, Uri> apiBaseAddressFactory)
            => services.AddHttpClient<IModulixApi, ModulixApi>((sp, httpClient) =>
            {
                var apiBaseAddress = apiBaseAddressFactory(sp);
                httpClient.BaseAddress = apiBaseAddress;
            });

        /// <summary>
        /// Adds the HTTP client to the <see cref="IModulixApi"/> without setting a base address.
        /// </summary>
        /// <remarks>It is recommended to set a base address using the returned <see cref="IHttpClientBuilder"/>.</remarks>
        /// <returns><see cref="IHttpClientBuilder"/> for additional configuring of the underlying <c>HttpClient</c>.</returns>
        public IHttpClientBuilder AddModulixApi()
            => services.AddHttpClient<IModulixApi, ModulixApi>();
    }
}