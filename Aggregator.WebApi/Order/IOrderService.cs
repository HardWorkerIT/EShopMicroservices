using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregator.WebApi.Order;

public interface IOrderService
{
    Task<List<OrderApi>> GetOrdersByCustomerIdAsync(int customerId, System.Threading.CancellationToken cancellationToken);
}
