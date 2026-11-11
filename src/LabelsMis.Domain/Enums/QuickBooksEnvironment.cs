namespace LabelsMis.Domain.Enums;

/// <summary>Which of Intuit's two worlds the app keys belong to. Sandbox keys only open sandbox
/// companies and production keys only open real ones.</summary>
public enum QuickBooksEnvironment
{
    Sandbox = 0,
    Production = 1
}
