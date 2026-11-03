using Microsoft.EntityFrameworkCore;
using Product.Microservice.Data;
using Xunit;
using System.Threading.Tasks;

public class ProductDatabaseTests
{
    [Fact]
    public void Context_constructor_does_not_connect_to_database()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().Options;
        using var context = new ApplicationDbContext(options);
        Assert.NotNull(context.Products);
    }
}
