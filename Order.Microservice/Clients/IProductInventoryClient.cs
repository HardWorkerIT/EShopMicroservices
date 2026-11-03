using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace Order.Microservice.Clients;

public interface IProductInventoryClient
{
    Task<HttpStatusCode> ReserveAsync(long productId, int quantity, Guid reservationId, CancellationToken cancellationToken);
    Task ReleaseAsync(long productId, int quantity, Guid reservationId, CancellationToken cancellationToken);
}
