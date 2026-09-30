using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using SQLite;

namespace TestAutoUpdateFramework
{
    public class Repository<T> : IDisposable where T : new()
    {


        private static Dictionary<Type, Action<T>> insertActions = [];
        private static Dictionary<Type, Action<T>> deleteActions = [];
        private static Dictionary<Type, Action<T>> updateActions = [];

        private Dictionary<ObservableCollection<T>, QueryExpressionsHolder> queries = [];

        private class QueryOrderExpression
        {
            public required string PropName { get; set; }
            public required bool IsDesc { get; set; }
        }

        private class OffsetDataHolder
        {
            public required int Offset { get; set; }
            public required int CurrentOffset { get; set; }
            public bool ReachedOffset => CurrentOffset >= Offset;
        }

        private class QueryExpressionsHolder()
        {
            public List<Func<T, bool>> WhereExpressions { get; set; } = [];
            public OffsetDataHolder? Offset { get; set; }
            public int? Limit { get; set; }
            public List<QueryOrderExpression> OrderByExpressions { get; set; } = [];
        }

        private class AsyncTableQueryWrapper(
            AsyncTableQuery<T> innerQuery,
            Dictionary<ObservableCollection<T>, QueryExpressionsHolder> queryWrappers) : IAsyncTableQueryWrapper<T>
        {

            protected QueryExpressionsHolder _queryExpressionsHolder = new();
            private int? _offset;
            private int? _limit;


            private AsyncTableQuery<T> _innerQuery = innerQuery;

            public IAsyncTableQueryWrapper<T> Where(Expression<Func<T, bool>> predExpr)
            {
                _innerQuery = _innerQuery.Where(predExpr);
                _queryExpressionsHolder.WhereExpressions.Add(predExpr.Compile());
                return this;
            }

            public IAsyncTableQueryWrapper<T> Skip(int n)
            {
                throw new NotSupportedException();
                // _innerQuery = _innerQuery.Skip(n);
                // _offset = n;
                // return this;
            }

            public IAsyncTableQueryWrapper<T> Take(int n)
            {
                throw new NotSupportedException();
                // _innerQuery = _innerQuery.Take(n);
                // _queryExpressionsHolder.Limit = n;
                // return this;
            }

            public IAsyncTableQueryWrapper<T> OrderBy<U>(Expression<Func<T, U>> orderExpr)
            {
                _innerQuery = _innerQuery.OrderBy(orderExpr);
                LambdaExpression lambdaExpression = orderExpr.NodeType == ExpressionType.Lambda
                    ? (LambdaExpression)orderExpr
                    : throw new NotSupportedException("Must be a predicate");
                MemberExpression memberExpression =
                    !(lambdaExpression.Body is UnaryExpression body) || body.NodeType != ExpressionType.Convert
                        ? lambdaExpression.Body as MemberExpression
                        : body.Operand as MemberExpression;
                _queryExpressionsHolder.OrderByExpressions.Add(new QueryOrderExpression()
                {
                    IsDesc = false,
                    PropName = memberExpression.Member.Name,
                });
                return this;
            }

            public IAsyncTableQueryWrapper<T> OrderByDescending<U>(Expression<Func<T, U>> orderExpr)
            {
                _innerQuery = _innerQuery.OrderByDescending(orderExpr);
                LambdaExpression lambdaExpression = orderExpr.NodeType == ExpressionType.Lambda
                    ? (LambdaExpression)orderExpr
                    : throw new NotSupportedException("Must be a predicate");
                MemberExpression memberExpression =
                    !(lambdaExpression.Body is UnaryExpression body) || body.NodeType != ExpressionType.Convert
                        ? lambdaExpression.Body as MemberExpression
                        : body.Operand as MemberExpression;
                _queryExpressionsHolder.OrderByExpressions.Add(new QueryOrderExpression()
                {
                    IsDesc = true,
                    PropName = memberExpression.Member.Name,
                });
                return this;
            }

            public async Task<ObservableCollection<T>> ToListAsync()
            {
                if (_offset != null)
                {
                    var currOffset = await _innerQuery.CountAsync();
                    _queryExpressionsHolder.Offset = new OffsetDataHolder
                    {
                        Offset = _offset.Value,
                        CurrentOffset = currOffset > _offset.Value ? _offset.Value : currOffset
                    };
                }
                
                var list = new ObservableCollection<T>(await _innerQuery.ToListAsync());
                queryWrappers[list] = _queryExpressionsHolder;
                return list;
            }

            public Task<T> FirstAsync()
            {
                throw new NotImplementedException();
            }

            public Task<T> FirstOrDefaultAsync()
            {
                throw new NotImplementedException();
            }

            public Task<T> ElementAtAsync(int index)
            {
                throw new NotImplementedException();
            }

            public Task<int> CountAsync(Expression<Func<T, bool>> predExpr)
            {
                throw new NotImplementedException();
            }
        }

        protected SQLiteAsyncConnection _connection;

        public Repository(SQLiteAsyncConnection connection)
        {
            _connection = connection;
            _connection.CreateTableAsync<T>().Wait();

            if (insertActions.GetValueOrDefault(typeof(T)) == null) insertActions[typeof(T)] = t => { };
            if (deleteActions.GetValueOrDefault(typeof(T)) == null) deleteActions[typeof(T)] = t => { };
            if (updateActions.GetValueOrDefault(typeof(T)) == null) updateActions[typeof(T)] = t => { };

            insertActions[typeof(T)] += async en => await OnInsert(en);
            deleteActions[typeof(T)] += async en => await OnDelete(en);
            updateActions[typeof(T)] += async en => await OnUpdate(en);
        }

        protected IAsyncTableQueryWrapper<T> GetTableQuery()
        {
            return new AsyncTableQueryWrapper(_connection.Table<T>(), queries);
        }

        public async Task Insert(T entity)
        {
            await _connection.InsertAsync(entity);
            insertActions[typeof(T)].Invoke(entity);
        }

        private async Task OnInsert(T entity)
        {
            foreach (var entry in queries)
            {
                var holder = entry.Value;
                var list = entry.Key;
                if (!CheckWhereExpressions(holder.WhereExpressions, entity)) continue;

                list.Add(entity);
                bool beenOrdered = HasOrderingSteps(holder);
                if(beenOrdered)
                    HandleOrderingFor(list, holder);
                //[1,2,3,4,5,6,7,8,9,10]
                //[1,2,2,3,4,5,6,7,8,9,10]
                // if (beenOrdered && holder.Offset != null)
                // {
                //     if (list[0].Equals(entity))
                //     {
                //         list.RemoveAt(0);
                //         var newEntity = await _connection.Table<T>().Skip(holder.Offset.Offset).Take(1).FirstAsync();
                //         list.Insert(0, newEntity);
                //     }
                // }
                
                
            }
        }

        public async Task DeleteAsync(T entity)
        {
            await _connection.DeleteAsync(entity);
            deleteActions[typeof(T)].Invoke(entity);
        }

        private async Task OnDelete(T entity)
        {
            foreach (var entry in queries)
            {
                entry.Key.Remove(entity);
            }
        }

        public async Task UpdateEntity(T entity)
        {
            await _connection.UpdateAsync(entity);
            updateActions[typeof(T)].Invoke(entity);
        }

        private async Task OnUpdate(T entity)
        {
            foreach (var entry in queries)
            {
                if (!CheckWhereExpressions(entry.Value.WhereExpressions, entity))
                {
                    for (int i = 0; i < entry.Key.Count; i++)
                    {
                        if (entry.Key[i].Equals(entity))
                        {
                            entry.Key.RemoveAt(i);
                            break;
                        }
                    }
                    continue;
                }
                
                if (!entry.Key.Contains(entity))
                {
                    entry.Key.Add(entity);
                }
                else
                {
                    for (int i = 0; i < entry.Key.Count; i++)
                    {
                        if (entry.Key[i].Equals(entity))
                        {
                            foreach (var property in entry.Key[i].GetType().GetProperties())
                            {
                                property.SetValue(entry.Key[i], property.GetValue(entity));
                            }
                        }
                    }
                }

                HandleOrderingFor(entry.Key, entry.Value);
            }
        }

        private bool CheckWhereExpressions(List<Func<T, bool>> predicates, T entity)
        {
            foreach (var whereExpression in predicates)
            {
                if (!whereExpression.Invoke(entity)) return false;
            }

            return true;
        }

        private bool HasOrderingSteps(QueryExpressionsHolder holder)
        {
            return holder.OrderByExpressions.Count > 0;
        } 

    private void HandleOrderingFor(ObservableCollection<T> collection, QueryExpressionsHolder holder)
        {
            foreach (var orderByExpression in holder.OrderByExpressions)
            {

                List<T> newList; 
                if(orderByExpression.IsDesc)
                    newList = collection.OrderByDescending(Val => Val.GetType().GetProperty(orderByExpression.PropName).GetValue(Val)).ToList();
                else
                    newList = collection.OrderBy(Val => Val.GetType().GetProperty(orderByExpression.PropName)).ToList();
                
                for (int i = 0; i < newList.Count; i++)
                {
                    if (!collection[i].Equals(newList[i]))
                    {
                        collection.RemoveAt(collection.IndexOf(newList[i]));
                        collection.Insert(i, newList[i]);   
                        break;
                    }
                }
               
            }
        }

        public void DisposeOfList(ObservableCollection<T> collection)
        {
            queries.Remove(collection);
        }

        public void Dispose()
        {
            insertActions[typeof(T)] -= async en => await OnInsert(en);
            deleteActions[typeof(T)] -= async en => await OnDelete(en);
            updateActions[typeof(T)] -= async en => await OnUpdate(en);
        }
    }
}
