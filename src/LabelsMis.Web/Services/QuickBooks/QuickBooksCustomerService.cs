using LabelsMis.Infrastructure.Persistence;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>Links LabelShop customers to QuickBooks customers: search QuickBooks' list, link one,
/// or create the customer in QuickBooks.</summary>
public class QuickBooksCustomerService(LabelsMisDbContext db, QuickBooksGateway gateway, ICurrentUserService currentUser)
{
    private readonly LabelsMisDbContext _db = db;
    private readonly QuickBooksGateway _gateway = gateway;
    private readonly ICurrentUserService _currentUser = currentUser;
}
