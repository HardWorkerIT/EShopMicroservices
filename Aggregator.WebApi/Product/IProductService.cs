using System.Threading.Tasks;

namespace Aggregator.WebApi.Product;

public interface IProductService
{
    Task<ProductApi> GetProductByIdAsync(int productId, System.Threading.CancellationToken cancellationToken);
}
