using AIAttributes.Sample.Garden.Pages;

namespace AIAttributes.Sample.Garden;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Detail pages — pushed on top of their parent tab
        Routing.RegisterRoute("product", typeof(ProductDetailPage));
        Routing.RegisterRoute("review", typeof(ProductReviewPage));
        Routing.RegisterRoute("order", typeof(OrderDetailPage));
        // Cart stays modal — slides up from anywhere
        Routing.RegisterRoute("cart", typeof(CartPage));
    }
}
