using System.Threading.Tasks;

namespace Aggregator.WebApi.Customer;

public interface ICustomerService
{
    Task<CustomerApi> GetCustomerByNameAsync(string customerName, System.Threading.CancellationToken cancellationToken);
}
