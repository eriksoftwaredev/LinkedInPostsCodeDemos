using System.Linq.Expressions;
using System.Reflection;
using System.Linq.Dynamic.Core;

// Data

#region Data

var employees = new List<Employee>
{
    new(Firstname: "Alice", Lastname: "Williams", Salary: 60000, Department: "IT", PerformanceRating: 4),
    new(Firstname: "Bob", Lastname: "Brown", Salary: 75000, Department: "HR", PerformanceRating: 3),
    new(Firstname: "Charlie", Lastname: "Taylor", Salary: 50000, Department: "Finance", PerformanceRating: 5),
};
var employeeSource = employees.AsQueryable();

var tasks = new List<Task>
{
    new(Title: "Weekly Team Update",
        Description: "Add updates from the previous week worth mentioning to the team and/or company all hands"),
    new(Title: "Project abc Status Report",
        Description: "give a quick summary of how the project has gone before the time period"),
};
var taskSource = tasks.AsQueryable();

#endregion

// 1. Using Runtime State within the Expression Tree

#region 1. Using Runtime State within the Expression Tree

decimal minSalary = 55000;
decimal maxSalary = 75000;

var employeeQuery = employeeSource
    .Where(x => x.Salary >= minSalary && x.Salary <= maxSalary);

Console.WriteLine("1. Using Runtime State within the Expression Tree:");
Console.WriteLine(string.Join(",", employeeQuery.Select(x => $"{x.Firstname} {x.Lastname}")));
// Output: Alice Williams,Bob Brown

#endregion

// 2. Calling Additional LINQ Methods

#region 2. Calling Additional LINQ Methods

bool sortByRating = true;
employeeQuery = employeeSource;

if (sortByRating)
    employeeQuery = employeeQuery.OrderBy(x => x.PerformanceRating);

Console.WriteLine("2. Calling Additional LINQ Methods:");
Console.WriteLine(string.Join(",", employeeQuery.Select(x => $"{x.Firstname} {x.Lastname}")));
// Output: Bob Brown,Alice Williams,Charlie Taylor

#endregion

// 3. Varying the Expression Tree Passed into LINQ Methods

#region 3. Varying the Expression Tree Passed into LINQ Methods

string targetDepartment = "IT";
int? targetRating = 4;

Expression<Func<Employee, bool>> expr = (targetDepartment, targetRating) switch
{
    ("" or null, null) => x => true,
    (_, null) => x => x.Department.Equals(targetDepartment),
    ("" or null, _) => x => x.PerformanceRating >= targetRating,
    (_, _) => x => x.Department.Equals(targetDepartment) && x.PerformanceRating >= targetRating
};

employeeQuery = employeeSource.Where(expr);

Console.WriteLine("3. Varying the Expression Tree Passed into LINQ Methods:");
Console.WriteLine(string.Join(",", employeeQuery.Select(x => $"{x.Firstname} {x.Lastname}")));
// Output: Alice Williams

#endregion

// 4. Constructing Expression Trees Using Factory Methods

#region 4. Constructing Expression Trees Using Factory Methods

string employeeSearchKeyword = "Alice";
string taskSearchKeyword = "Project abc";

IQueryable<T> TextFilter<T>(IQueryable<T> source, string term)
{
    if (string.IsNullOrEmpty(term))
        return source;

    // T stands for the type of element in the query, decided at compile time
    Type elementType = typeof(T);

    // Retrieve all string properties from this specific type
    PropertyInfo[] stringProperties =
        elementType.GetProperties()
            .Where(x => x.PropertyType == typeof(string))
            .ToArray();
    if (!stringProperties.Any())
        return source;

    // Identify the correct String.Contains overload
    MethodInfo containsMethod =
        typeof(string).GetMethod("Contains", new[] { typeof(string) })!;

    // Create a parameter for the expression tree, represented as 'x' in 'x => x.PropertyName.Contains("term")'
    // Define a ParameterExpression object
    ParameterExpression prm = Expression.Parameter(elementType);

    // Map each property to an expression tree node
    IEnumerable<Expression> expressions = stringProperties
        .Select<PropertyInfo, Expression>(prp =>
            // Construct an expression tree node for each property, like x.PropertyName.Contains("term")
            Expression.Call( // .Contains(...) 
                Expression.Property( // .PropertyName
                    prm, // x 
                    prp
                ),
                containsMethod,
                Expression.Constant(term) // "term" 
            )
        );

    // Combine all the resulting expression nodes using || (OR operator).
    Expression body = expressions
        .Aggregate(
            (prev, current) => Expression.Or(prev, current)
        );

    // Encapsulate the expression body in a compile-time-typed lambda expression
    Expression<Func<T, bool>> lambda =
        Expression.Lambda<Func<T, bool>>(body, prm);

    // Because the lambda is compile-time-typed (albeit with a generic parameter), we can use it with the Where method
    return source.Where(lambda);
}

Console.WriteLine("4. Constructing Expression Trees Using Factory Methods:");

employeeQuery = TextFilter(employeeSource, employeeSearchKeyword);
Console.WriteLine(string.Join(",", employeeQuery.Select(x => $"{x.Firstname} {x.Lastname}")));
// Output: Alice Williams

var taskQuery = TextFilter(taskSource, taskSearchKeyword);
Console.WriteLine(string.Join(",",
    taskQuery.Select(x => $"Task Detail:\n\tTitle: {x.Title}\n\tDescription: {x.Description}\n")));
// Output: Task Detail:
//              Title: Project abc Status Report
//              Description: give a quick summary of how the project has gone before the time period

#endregion

// 5. Adding Method Call Nodes to IQueryable's Expression Tree:

#region 5. Adding Method Call Nodes to IQueryable's Expression Tree

IQueryable TextFilter_Untyped(IQueryable source, string term)
{
    if (string.IsNullOrEmpty(term))
        return source;

    Type elementType = source.ElementType;

    // Retrieve all string properties from this specific type
    PropertyInfo[] stringProperties =
        elementType.GetProperties()
            .Where(x => x.PropertyType == typeof(string))
            .ToArray();
    if (!stringProperties.Any())
        return source;

    // Identify the correct String.Contains overload
    MethodInfo containsMethod =
        typeof(string).GetMethod("Contains", new[] { typeof(string) })!;

    // Create a parameter for the expression tree, represented as 'x' in 'x => x.PropertyName.Contains("term")'
    // Define a ParameterExpression object
    ParameterExpression prm = Expression.Parameter(elementType);

    // Map each property to an expression tree node
    IEnumerable<Expression> expressions = stringProperties
        .Select<PropertyInfo, Expression>(prp =>
            // Construct an expression tree node for each property, like x.PropertyName.Contains("term")
            Expression.Call( // .Contains(...) 
                Expression.Property( // .PropertyName
                    prm, // x 
                    prp
                ),
                containsMethod,
                Expression.Constant(term) // "term" 
            )
        );

    // Combine all the resulting expression nodes using || (OR operator).
    Expression body = expressions
        .Aggregate(
            (prev, current) => Expression.Or(prev, current)
        );
    if (body is null)
        return source;

    Expression filteredTree = Expression.Call(
        typeof(Queryable),
        "Where",
        new[] { elementType },
        source.Expression,
        Expression.Lambda(body, prm!)
    );

    return source.Provider.CreateQuery(filteredTree);
}

var eQuery = TextFilter_Untyped(employeeSource, "Charlie");

Console.WriteLine("5. Adding Method Call Nodes to IQueryable's Expression Tree:");
Console.WriteLine(string.Join(",", eQuery.Cast<Employee>().Select(x => $"{x.Firstname} {x.Lastname}")));
// Output: Charlie Taylor

#endregion

// 6. Leveraging the Dynamic LINQ Library

#region 6. Leveraging the Dynamic LINQ Library

IQueryable TextFilter_Strings(IQueryable source, string term) {
    if (string.IsNullOrEmpty(term)) 
     return source; 

    var elementType = source.ElementType;

    // Retrieve all string properties from this specific type
    var stringProperties = 
        elementType.GetProperties()
            .Where(x => x.PropertyType == typeof(string))
            .ToArray();
    if (!stringProperties.Any()) { return source; }

    // Build the string expression
    string filterExpr = string.Join(" || ",
        stringProperties.Select(prp => $"{prp.Name}.Contains(@0)"));
    
    return source.Where(filterExpr, term);
}

var qry = TextFilter_Untyped(employeeSource, "HR");

Console.WriteLine("6. Leveraging the Dynamic LINQ Library:");
Console.WriteLine(string.Join(",", qry.Cast<Employee>().Select(x => $"{x.Firstname} {x.Lastname}")));
// Output: Bob Brown

#endregion

record Employee(string Firstname, string Lastname, decimal Salary, string Department, int? PerformanceRating);

record Task(string Title, string Description);