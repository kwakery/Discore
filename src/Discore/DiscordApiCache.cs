﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Discore
{
    /// <summary>
    /// A client-side cache for data coming from the Discord API.
    /// </summary>
    public class DiscordApiCache
    {
        /// <summary>
        /// Gets a new empty <see cref="DiscordApiCache"/>.
        /// </summary>
        public static DiscordApiCache Empty { get { return new DiscordApiCache(); } }

        ConcurrentDictionary<Type, ConcurrentDictionary<string, ICacheable>> cache;
        ConcurrentDictionary<Type, ConcurrentDictionary<string, DiscordApiCache>> innerCaches;

        /// <summary>
        /// Creates a new <see cref="DiscordApiCache"/> instance.
        /// </summary>
        public DiscordApiCache()
        {
            cache = new ConcurrentDictionary<Type, ConcurrentDictionary<string, ICacheable>>();
            innerCaches = new ConcurrentDictionary<Type, ConcurrentDictionary<string, DiscordApiCache>>();
        }

        #region GetList
        /// <summary>
        /// Gets a list of all <see cref="ICacheable"/>s of the specified type.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/>.</typeparam>
        /// <returns>Returns all <see cref="ICacheable"/>s of the given type.</returns>
        public IReadOnlyList<KeyValuePair<string, T>> GetList<T>()
            where T : class, ICacheable
        {
            List<KeyValuePair<string, T>> list = new List<KeyValuePair<string, T>>();

            Type type = typeof(T);
            ConcurrentDictionary<string, ICacheable> typeCache;
            if (cache.TryGetValue(type, out typeCache))
            {
                foreach (KeyValuePair<string, ICacheable> pair in typeCache)
                    list.Add(new KeyValuePair<string, T>(pair.Key, (T)pair.Value));
            }

            return list;
        }

        /// <summary>
        /// Gets a list of all <see cref="ICacheable"/>s of the specified type nested
        /// in the given parent <see cref="ICacheable"/>.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/>.</typeparam>
        /// <typeparam name="U">The type of the parent <see cref="ICacheable"/>.</typeparam>
        /// <param name="parentCacheable">The <see cref="ICacheable"/> parent to search under.</param>
        /// <returns>Returns all <see cref="ICacheable"/>s of the given type nested under the given parent.</returns>
        public IReadOnlyList<KeyValuePair<string, T>> GetList<T, U>(U parentCacheable)
            where T : class, ICacheable
            where U : class, ICacheable
        {
            Type type = typeof(U);
            ConcurrentDictionary<string, DiscordApiCache> innerTypeCache;
            if (innerCaches.TryGetValue(type, out innerTypeCache))
            {
                DiscordApiCache innerCache;
                if (innerTypeCache.TryGetValue(parentCacheable.Id, out innerCache))
                    return innerCache.GetList<T>();
            }

            return new KeyValuePair<string, T>[0];
        }
        #endregion

        #region TryGet
        /// <summary>
        /// Attempts to find the specified <see cref="ICacheable"/> by its id.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/>.</typeparam>
        /// <param name="id">The id of the <see cref="ICacheable"/>.</param>
        /// <param name="cacheable">The found <see cref="ICacheable"/>.</param>
        /// <returns>Returns whether or not the <see cref="ICacheable"/> was found.</returns>
        public bool TryGet<T>(string id, out T cacheable)
            where T : class, ICacheable
        {
            Type type = typeof(T);
            ConcurrentDictionary<string, ICacheable> typeCache;
            if (cache.TryGetValue(type, out typeCache))
            {
                ICacheable value;
                if (typeCache.TryGetValue(id, out value))
                {
                    cacheable = value as T;
                    return true;
                }
            }

            cacheable = null;
            return false;
        }

        /// <summary>
        /// Attempts to find the specified <see cref="ICacheable"/> nested under 
        /// the given parent <see cref="ICacheable"/>.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/>.</typeparam>
        /// <typeparam name="U">The type of the parent <see cref="ICacheable"/>.</typeparam>
        /// <param name="parentCacheable">The <see cref="ICacheable"/> parent to search under.</param>
        /// <param name="childId">The id of the child <see cref="ICacheable"/> to locate.</param>
        /// <param name="cacheable">The found <see cref="ICacheable"/>.</param>
        /// <returns>Returns whether or not the <see cref="ICacheable"/> was found.</returns>
        public bool TryGet<T, U>(U parentCacheable, string childId, out T cacheable)
            where T : class, ICacheable
            where U : class, ICacheable
        {
            Type type = typeof(U);
            ConcurrentDictionary<string, DiscordApiCache> innerTypeCache;
            if (innerCaches.TryGetValue(type, out innerTypeCache))
            {
                DiscordApiCache innerCache;
                if (innerTypeCache.TryGetValue(parentCacheable.Id, out innerCache))
                    return innerCache.TryGet(childId, out cacheable);
            }

            cacheable = null;
            return false;
        }
        #endregion

        #region GetAndTryUpdate
        /// <summary>
        /// Attempts to get and update the specified <see cref="ICacheable"/> simultaneously.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/> to get and update.</typeparam>
        /// <param name="id">The id of the <see cref="ICacheable"/> to get and update.</param>
        /// <param name="data">The <see cref="DiscordApiData"/> to update the <see cref="ICacheable"/> with.</param>
        /// <param name="cacheable">The found <see cref="ICacheable"/>.</param>
        /// <returns>Returns whether or not the <see cref="ICacheable"/> was found and updated.</returns>
        public bool GetAndTryUpdate<T>(string id, DiscordApiData data, out T cacheable)
            where T : class, ICacheable
        {
            Type type = typeof(T);
            ConcurrentDictionary<string, ICacheable> typeCache;
            if (cache.TryGetValue(type, out typeCache))
            {
                ICacheable existingCacheable;
                if (typeCache.TryGetValue(id, out existingCacheable))
                {
                    existingCacheable.Update(data);
                    cacheable = existingCacheable as T;
                    return true;
                }
            }
            
            cacheable = null;
            return false;
        }

        /// <summary>
        /// Attempts to get and update the specified <see cref="ICacheable"/>
        /// nested under the given parent <see cref="ICacheable"/> simultaneously.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/> to get and update.</typeparam>
        /// <typeparam name="U">The type of the parent <see cref="ICacheable"/>.</typeparam>
        /// <param name="parentCacheable">The <see cref="ICacheable"/> parent to search under.</param>
        /// <param name="childId">The id of the <see cref="ICacheable"/> to get and update.</param>
        /// <param name="data">The <see cref="DiscordApiData"/> to update the <see cref="ICacheable"/> with.</param>
        /// <param name="cacheable">The found <see cref="ICacheable"/>.</param>
        /// <returns>Returns whether or not the <see cref="ICacheable"/> was found and updated.</returns>
        public bool GetAndTryUpdate<T, U>(U parentCacheable, string childId, DiscordApiData data, out T cacheable)
            where T : class, ICacheable
            where U : class, ICacheable
        {
            Type type = typeof(U);
            ConcurrentDictionary<string, DiscordApiCache> innerTypeCache;
            if (innerCaches.TryGetValue(type, out innerTypeCache))
            {
                DiscordApiCache innerCache;
                if (innerTypeCache.TryGetValue(parentCacheable.Id, out innerCache))
                    return innerCache.GetAndTryUpdate(childId, data, out cacheable);
            }

            cacheable = null;
            return false;
        }
        #endregion

        #region AddOrUpdate
        /// <summary>
        /// Attempts to add or update an existing <see cref="ICacheable"/>.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/> to add or update.</typeparam>
        /// <param name="id">The id of the <see cref="ICacheable"/>.</param>
        /// <param name="data">The data to update the <see cref="ICacheable"/> with.</param>
        /// <param name="createCallback">A callback used to create the <see cref="ICacheable"/> if it does not exist.</param>
        /// <returns>Returns the added or updated <see cref="ICacheable"/>.</returns>
        public T AddOrUpdate<T>(string id, DiscordApiData data, Func<T> createCallback)
            where T : class, ICacheable
        {
            Type type = typeof(T);
            ConcurrentDictionary<string, ICacheable> typeCache;
            if (!cache.TryGetValue(type, out typeCache))
            {
                // Type cache doesnt exist, so add it
                typeCache = new ConcurrentDictionary<string, ICacheable>();
                if (!cache.TryAdd(type, typeCache))
                    typeCache = cache[type];
            }

            ICacheable existingCacheable;
            if (!typeCache.TryGetValue(id, out existingCacheable))
            {
                // Object is not cached, so add it
                T cacheable = createCallback();
                if (!typeCache.TryAdd(id, cacheable))
                {
                    // Failed to add, so update the existing
                    ICacheable existing = typeCache[id];
                    existing.Update(data);
                    return existing as T;
                }
                else
                {
                    // Update the added one if successful
                    cacheable.Update(data);
                    return cacheable;
                }
            }
            else
            {
                // Update the existing
                existingCacheable.Update(data);
                return existingCacheable as T;
            }
        }

        /// <summary>
        /// Attempts to add or update an existing <see cref="ICacheable"/> nested under a parent <see cref="ICacheable"/>.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/> to add or update.</typeparam>
        /// <typeparam name="U">The type of the parent <see cref="ICacheable"/>.</typeparam>
        /// <param name="parentCacheable">The <see cref="ICacheable"/> parent to search under.</param>
        /// <param name="childId">The id of the <see cref="ICacheable"/>.</param>
        /// <param name="data">The data to update the <see cref="ICacheable"/> with.</param>
        /// <param name="createCallback">A callback used to create the <see cref="ICacheable"/> if it does not exist.</param>
        /// <param name="makeGlobalAlias">Whether or not a global reference to the <see cref="ICacheable"/> 
        /// without the parent should be set.</param>
        /// <returns>Returns the added or updated <see cref="ICacheable"/>.</returns>
        public T AddOrUpdate<T, U>(U parentCacheable, string childId, DiscordApiData data, Func<T> createCallback, 
            bool makeGlobalAlias = false)
            where T : class, ICacheable
            where U : class, ICacheable
        {
            Type type = typeof(U);
            ConcurrentDictionary<string, DiscordApiCache> innerTypeCache;
            if (!innerCaches.TryGetValue(type, out innerTypeCache))
            {
                // Inner type cache doesnt exist, so add it
                innerTypeCache = new ConcurrentDictionary<string, DiscordApiCache>();
                if (!innerCaches.TryAdd(type, innerTypeCache))
                    innerTypeCache = innerCaches[type];
            }

            DiscordApiCache innerCache;
            if (!innerTypeCache.TryGetValue(parentCacheable.Id, out innerCache))
            {
                // Inner cache doesn't exist, so add it
                innerCache = new DiscordApiCache();
                if (!innerTypeCache.TryAdd(parentCacheable.Id, innerCache))
                {
                    // Failed to add, so use the existing
                    innerCache = innerTypeCache[parentCacheable.Id];
                }
            }

            T cacheable = innerCache.AddOrUpdate(childId, data, createCallback);

            if (makeGlobalAlias)
                SetAlias(cacheable);

            return cacheable;
        }
        #endregion

        #region TryRemove
        /// <summary>
        /// Attempts to removed a <see cref="ICacheable"/> by its id.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/> to remove.</typeparam>
        /// <param name="id">The id of the <see cref="ICacheable"/> to remove.</param>
        /// <param name="cacheable">The removed <see cref="ICacheable"/>.</param>
        /// <returns>Returns whether or not the <see cref="ICacheable"/> was deleted.</returns>
        public bool TryRemove<T>(string id, out T cacheable)
            where T : class, ICacheable
        {
            Type type = typeof(T);
            ConcurrentDictionary<string, ICacheable> typeCache;
            if (cache.TryGetValue(type, out typeCache))
            {
                ICacheable existingCacheable;
                if (typeCache.TryGetValue(id, out existingCacheable))
                {
                    cacheable = existingCacheable as T;
                    return true;
                }
            }

            cacheable = null;
            return false;
        }

        /// <summary>
        /// Attempts to removed a <see cref="ICacheable"/> nested under a parent <see cref="ICacheable"/> by its id.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/> to remove.</typeparam>
        /// <typeparam name="U">The type of the parent <see cref="ICacheable"/>.</typeparam>
        /// <param name="parentCacheable">The <see cref="ICacheable"/> parent to search under.</param>
        /// <param name="childId">The id of the <see cref="ICacheable"/> to remove.</param>
        /// <param name="cacheable">The removed <see cref="ICacheable"/>.</param>
        /// <returns>Returns whether or not the <see cref="ICacheable"/> was deleted.</returns>
        public bool TryRemove<T, U>(U parentCacheable, string childId, out T cacheable)
            where T : class, ICacheable
            where U : class, ICacheable
        {
            Type type = typeof(U);
            ConcurrentDictionary<string, DiscordApiCache> innerTypeCache;
            if (!innerCaches.TryGetValue(type, out innerTypeCache))
            {
                cacheable = null;
                return false;
            }

            DiscordApiCache innerCache;
            if (!innerTypeCache.TryGetValue(parentCacheable.Id, out innerCache))
            {
                // Inner cache doesn't exist, so just try and remove alias
                return TryRemove(childId, out cacheable);
            }
            else
            {
                // Delete alias
                T temp;
                TryRemove(childId, out temp);

                // Delete cacheable from inner cache
                return innerCache.TryRemove(childId, out cacheable);
            }
        }
        #endregion

        #region Aliases
        /// <summary>
        /// Sets an alias in the cache that will point to the specified <see cref="ICacheable"/>.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/> to make an alias for.</typeparam>
        /// <param name="cacheable">The <see cref="ICacheable"/> to make an alias for.</param>
        /// <remarks>
        /// This is used to have data available at multiple levels. For instance, if you had
        /// data cached under a parent, you could use this method to make an alias-reference to that
        /// nested data that would not be nested under a parent.
        /// This should only be used when the nested data has a globally unique id that is not
        /// reliant on its parent.
        /// </remarks>
        public void SetAlias<T>(T cacheable)
            where T : class, ICacheable
        {
            Type type = typeof(T);
            ConcurrentDictionary<string, ICacheable> typeCache;
            if (!cache.TryGetValue(type, out typeCache))
            {
                // Type cache doesnt exist, so add it
                typeCache = new ConcurrentDictionary<string, ICacheable>();
                if (!cache.TryAdd(type, typeCache))
                    typeCache = cache[type];
            }

            ICacheable existingCacheable;
            if (!typeCache.TryGetValue(cacheable.Id, out existingCacheable))
            {
                // Object is not cached, so add it
                if (!typeCache.TryAdd(cacheable.Id, cacheable))
                {
                    // Failed to add, but the alias must be the same
                    // as the passed cacheable, so overwrite it.
                    typeCache[cacheable.Id] = cacheable;
                }
            }
            else
            {
                if (!object.ReferenceEquals(existingCacheable, cacheable))
                    // Existing alias differs from passed cacheable,
                    // so we will overwrite it.
                    typeCache[cacheable.Id] = cacheable;
            }
        }

        /// <summary>
        /// Attempts to remove an alias to an <see cref="ICacheable"/>.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="ICacheable"/> to remove the reference to.</typeparam>
        /// <param name="id">The id of the <see cref="ICacheable"/> alias.</param>
        /// <returns>Returns whether or not the alias was removed.</returns>
        public bool TryRemoveAlias<T>(string id)
           where T : class, ICacheable
        {
            Type type = typeof(T);
            ConcurrentDictionary<string, ICacheable> typeCache;
            if (cache.TryGetValue(type, out typeCache))
            {
                ICacheable existingCacheable;
                if (typeCache.TryGetValue(id, out existingCacheable))
                    return true;
            }

            return false;
        }
        #endregion

        /// <summary>
        /// Clears all data in this <see cref="DiscordApiCache"/>.
        /// </summary>
        public void Clear()
        {
            cache.Clear();
            innerCaches.Clear();
        }
    }
}