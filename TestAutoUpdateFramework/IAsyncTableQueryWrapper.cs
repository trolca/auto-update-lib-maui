using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace TestAutoUpdateFramework;

public interface IAsyncTableQueryWrapper<T> where T : new()
{
    /// <summary>Filters the query based on a predicate.</summary>
    public IAsyncTableQueryWrapper<T> Where(Expression<Func<T, bool>> predExpr);
    /// <summary>
    /// Skips a given number of elements from the query and then yields the remainder.
    /// </summary>
    public IAsyncTableQueryWrapper<T> Skip(int n);
    /// <summary>
    /// Yields a given number of elements from the query and then skips the remainder.
    /// </summary>
    public IAsyncTableQueryWrapper<T> Take(int n);

    /// <summary>Order the query results according to a key.</summary>
    public IAsyncTableQueryWrapper<T> OrderBy<U>(Expression<Func<T, U>> orderExpr);

    /// <summary>Order the query results according to a key.</summary>
    public IAsyncTableQueryWrapper<T> OrderByDescending<U>(Expression<Func<T, U>> orderExpr);

    /// <summary>Queries the database and returns the results as a List.</summary>
    public Task<ObservableCollection<T>> ToListAsync();
    /// <summary>Returns the first element of this query.</summary>
    public Task<T> FirstAsync();
    /// <summary>
    /// Returns the first element of this query, or null if no element is found.
    /// </summary>
    public Task<T> FirstOrDefaultAsync();

    /// <summary>Returns the element at a given index</summary>
    public Task<T> ElementAtAsync(int index);

    /// <summary>
    /// Execute SELECT COUNT(*) on the query with an additional WHERE clause.
    /// </summary>
    public Task<int> CountAsync(Expression<Func<T, bool>> predExpr);

}