﻿using LinqKit;
using Sorocaba.Commons.Entity.Reflection;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.SqlServer;
using System.Linq;
using System.Linq.Dynamic;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Sorocaba.Commons.Entity.Pagination {
    public static class PaginationEngine {

        public static PaginatedResult<T> Paginate<T>(IQueryable<T> query, HttpRequestMessage request) where T : class {
            return Paginate<T>(query, ParametersParser.FromRequest(request));
        }

        public static PaginatedResult<T> Paginate<T>(IQueryable<T> query, PaginationParameters parameters) where T : class {

            ICollection<Sorter> sorters = parameters.Sorters;
            if (sorters != null && sorters.Count > 0) {
                StringBuilder sortString = new StringBuilder();
                foreach (var sorter in sorters) {
                    if (sortString.Length > 0) {
                        sortString.Append(", ");
                    }
                    sortString.Append(sorter.FieldName);
                    sortString.Append(' ');
                    sortString.Append(sorter.SortOrder);
                }
                query = query.OrderBy(sortString.ToString(), null);
            }

            ICollection<Filter> filters = parameters.Filters;
            if (filters != null && filters.Count > 0) {
                foreach (var filter in filters) {
                    var filterExpression = ParseFilter<T>(filter);
                    query = query.Where(filterExpression);
                }
            }

            ICollection<Filter> fullFilters = parameters.FullFilters;
            if (fullFilters != null && fullFilters.Count > 0) {
                var finalPredicate = PredicateBuilder.False<T>();
                foreach (var fullFilter in fullFilters) {
                    var filterExpression = ParseFilter<T>(fullFilter, true);
                    if (filterExpression != null) {
                        finalPredicate = finalPredicate.Or(filterExpression);
                    }
                }
                query = query.Where(finalPredicate.Expand());
            }

            int page = parameters.Page;
            int itensPerPage = parameters.ItensPerPage;
            long itemCount = query.Count();
            int pageCount = (int) Math.Ceiling((double) itemCount / (double) itensPerPage);
            if (page > pageCount) {
                page = pageCount;
            }
            if (page == 0) {
                page = 1;
            }
            int itemOffset = ((page - 1) * itensPerPage) + 1;
            itemOffset = (itemOffset <= 0) ? 0 : itemOffset;

            List<T> itemList = query.Skip((page - 1) * itensPerPage).Take(itensPerPage).ToList();

            return new PaginatedResult<T> {
                ItemCount = itemCount,
                PageCount = pageCount,
                CurrentPage = page,
                ItemOffset = itemOffset,
                ItemList = itemList
            };
        }

        private static Expression<Func<T, bool>> ParseFilter<T>(Filter filter, bool nullOnConvertError = false) where T : class {

            string fName = filter.FieldName;
            string fOperator = filter.Operator;
            object fValue = filter.FieldValue;

            Type entity = typeof(T);

            bool propHasCollections;
            PropertyInfo propInfo = TypeUtils.GetPropertyInfo<T>(fName, out propHasCollections);
            Type propType = (propInfo != null) ? propInfo.PropertyType : null;

            if (propInfo == null) {
                Exception(Strings.EntityPropertyInvalid(fName));
            }

            if (propHasCollections) {
                Exception(Strings.CollectionPropertyNotSupported(fName));
            }

            if (!propType.IsValueType && propType != typeof(string)) {
                Exception(Strings.PropertyMustBeValueType(fName));
            }

            object oldFValue = fValue;
            if (!TypeUtils.TryConvert(propType, fValue, out fValue)) {
                if (nullOnConvertError) {
                    return null;
                } else {
                    Exception(Strings.CouldNotConvertValueToPropertyType(oldFValue.ToString(), propType.Name, fName));
                }
            }


            ParameterExpression entityExpression;
            Expression propertyExpression = TypeUtils.GetPropertyExpression<T>(fName, out entityExpression);
            ConstantExpression argumentExpresson = Expression.Constant(fValue);

            if (fOperator == Operators.LIKE.Symbol || fOperator == Operators.LIKE_ALTERNATIVE.Symbol) {

                argumentExpresson = Expression.Constant(fValue.ToString());

                if (propType == (typeof(bool)) || propType == (typeof(bool?))) {
                    Exception(Strings.LikeNotSupportedForDataType(propType.Name, fName));
                }

                else if (propType == (typeof(char)) || propType == (typeof(char?))) {
                    Exception(Strings.LikeNotSupportedForDataType(propType.Name, fName));
                }

                else if (propType == (typeof(DateTime)) || propType == (typeof(DateTime?))) {
                    Exception(Strings.LikeNotSupportedForDataType(propType.Name, fName));
                }
                
                else if(propType != typeof(string)) {
                    var convertiblePropertyType = typeof(decimal?);
                    var convertedProperty = Expression.Convert(propertyExpression, convertiblePropertyType);
                    var conversionMethod = typeof(SqlFunctions).GetMethod("StringConvert", new Type[] { convertiblePropertyType, typeof(int?) });
                    propertyExpression = Expression.Call(conversionMethod, convertedProperty, Expression.Constant(20, typeof(int?)));
                }
            }

            return Expression.Lambda<Func<T, bool>>(
                Operators.GetBySymbol(fOperator).Expression(propertyExpression, argumentExpresson),
                entityExpression
            );
        }

        private static void Exception(string message, params object[] args) {
            throw new PaginationException(message);
        }
    }
}