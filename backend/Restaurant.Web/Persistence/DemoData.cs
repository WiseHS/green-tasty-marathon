using APIHandler.Entities;
using APIHandler.Constants.Dishes;
using APIHandler.Models.WaiterList;
using Amazon.CognitoIdentityProvider.Model;
using Restaurant.Web.Auth;

namespace Restaurant.Web.Persistence;

public static class DemoData
{
    public static async Task Seed(PostgresStore db,PostgresIdentity identity)
    {
        var password=Environment.GetEnvironmentVariable("DEMO_WAITER_PASSWORD") ?? throw new InvalidOperationException("Set DEMO_WAITER_PASSWORD before seeding");
        const string locationId="demo-kyiv";
        await db.Save(new Location { Id=locationId,Address="Kyiv, Khreshchatyk 12",Description="Green & Tasty — навчальний ресторан",
            TotalCapacity=24,AverageOccupancy="40%",Rating=4.8,ImageUrl="/assets/img/restaurant.png",SpecialtyDishIds=["demo-salad","demo-pasta","demo-dessert"] },true);
        for(var i=1;i<=6;i++)await db.Save(new Table { Id=$"demo-table-{i}",LocationId=locationId,Capacity=4,TableNumber=i },true);
        var menu=new[] { ("demo-salad","Green salad",DishType.Appetizer,12m),("demo-pasta","Vegetable pasta",DishType.MainCourse,18m),("demo-dessert","Berry dessert",DishType.Dessert,9m) };
        foreach(var (id,name,type,price) in menu)await db.Save(new Dish
        { Id=id,Name=name,Description="Навчальна позиція меню",DishType=type,Price=price,Weight="250 g",Calories="320 kcal",
            Proteins="12 g",Fats="10 g",Carbohydrates="40 g",Vitamins="A, C",ImageUrl="/assets/img/dish.png",State=DishState.Available,LocationId=locationId },true);
        const string email="demo-waiter@example.com";
        try { await identity.SignUpAsync(new SignUpRequest { Username=email,Password=password,UserAttributes=[new() { Name="given_name",Value="Demo" },new() { Name="family_name",Value="Waiter" }] }); }
        catch(UsernameExistsException) { }
        await identity.AdminAddUserToGroupAsync(new AdminAddUserToGroupRequest { Username=email,GroupName="Waiter" });
        var waiter=await identity.AdminGetUserAsync(new AdminGetUserRequest { Username=email });
        await db.Save(new WaiterListItem { UserId=waiter.UserAttributes.Single(a=>a.Name=="sub").Value,LocationId=locationId,CreatedAt=DateTime.UtcNow },true);
    }
}
