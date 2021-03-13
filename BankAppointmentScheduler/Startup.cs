using BankAppointmentScheduler.Administrative.BankManagement;
using BankAppointmentScheduler.Administrative.BranchManagement;
using BankAppointmentScheduler.Administrative.CounterService;
using BankAppointmentScheduler.Administrative.ServiceManagement;
using BankAppointmentScheduler.BankSchedulerService;
using BankAppointmentScheduler.Domain;
using BankAppointmentScheduler.Persistence;
using BankAppointmentScheduler.RealtimeQueueService;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Serialization;

namespace BankAppointmentScheduler.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();

            services.AddDbContext<IBankAppointmentContext, postgresContext>(options =>
                options.UseNpgsql(Configuration.GetConnectionString("connectionString")));

            services.AddTransient<IBankRealtimeQueueService, BankRealtimeQueueService>();
            services.AddTransient<IBankAppointmentSchedulerService, BankAppointmentSchedulerService>();

            services.AddTransient<IBankManagement, BankManagement>();
            services.AddTransient<IBranchManagement, BranchManagement>();
            services.AddTransient<ICounterManagement, CounterManagement>();
            services.AddTransient<IServiceManagement, ServiceManagement>();

            //services.AddDbContext<BankAppointmentContext>(options =>
            //    options.UseNpgsql(Configuration.GetConnectionString("connectionString")));

            ////services.AddDbContext<BankAppointmentContext>(options =>
            ////    options.UseNpgsql(Configuration.GetConnectionString("connectionString")));

            //services.AddIdentity<ApplicationUser, ApplicationUserRole>()
            //    .AddEntityFrameworkStores<BankAppointmentContext> ()
            //.AddUserManager<UserManager<ApplicationUser>>()
            //.AddDefaultTokenProviders();

            //services.AddIdentity<ApplicationUser, ApplicationUserRole>()
            //    .AddEntityFrameworkStores<BankAppointmentContext>()
            //    .AddDefaultTokenProviders();

            #region Configure Api 

            services.AddControllers(setupAction =>
            {
                setupAction.ReturnHttpNotAcceptable = true;

            }).AddNewtonsoftJson(setupAction =>
            {
                setupAction.SerializerSettings.ContractResolver =
                   new CamelCasePropertyNamesContractResolver();
            })
             .AddXmlDataContractSerializerFormatters()
            .ConfigureApiBehaviorOptions(setupAction =>
            {
                setupAction.InvalidModelStateResponseFactory = context =>
                {
                    // create a problem details object
                    var problemDetailsFactory = context.HttpContext.RequestServices
                        .GetRequiredService<ProblemDetailsFactory>();
                    var problemDetails = problemDetailsFactory.CreateValidationProblemDetails(
                            context.HttpContext,
                            context.ModelState);

                    // add additional info not added by default
                    problemDetails.Detail = "See the errors field for details.";
                    problemDetails.Instance = context.HttpContext.Request.Path;

                    // find out which status code to use
                    var actionExecutingContext =
                          context as Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext;

                    // if there are modelstate errors & all keys were correctly
                    // found/parsed we're dealing with validation errors
                    //
                    // if the context couldn't be cast to an ActionExecutingContext
                    // because it's a ControllerContext, we're dealing with an issue 
                    // that happened after the initial input was correctly parsed.  
                    // This happens, for example, when manually validating an object inside
                    // of a controller action.  That means that by then all keys
                    // WERE correctly found and parsed.  In that case, we're
                    // thus also dealing with a validation error.
                    if (context.ModelState.ErrorCount > 0 &&
                        (context is ControllerContext ||
                         actionExecutingContext?.ActionArguments.Count == context.ActionDescriptor.Parameters.Count))
                    {
                        problemDetails.Type = "https://courselibrary.com/modelvalidationproblem";
                        problemDetails.Status = StatusCodes.Status422UnprocessableEntity;
                        problemDetails.Title = "One or more validation errors occurred.";

                        return new UnprocessableEntityObjectResult(problemDetails)
                        {
                            ContentTypes = { "application/problem+json" }
                        };
                    }

                    // if one of the keys wasn't correctly found / couldn't be parsed
                    // we're dealing with null/unparsable input
                    problemDetails.Status = StatusCodes.Status400BadRequest;
                    problemDetails.Title = "One or more errors on input occurred.";
                    return new BadRequestObjectResult(problemDetails)
                    {
                        ContentTypes = { "application/problem+json" }
                    };
                };
            });

            #endregion
        }


        internal static IActionResult ProblemDetailsInvalidModelStateResponse(
            ProblemDetailsFactory problemDetailsFactory, ActionContext context)
        {
            var problemDetails = problemDetailsFactory.CreateValidationProblemDetails(context.HttpContext, context.ModelState);
            ObjectResult result;
            if (problemDetails.Status == 400)
            {
                // For compatibility with 2.x, continue producing BadRequestObjectResult instances if the status code is 400.
                result = new BadRequestObjectResult(problemDetails);
            }
            else
            {
                result = new ObjectResult(problemDetails);
            }
            result.ContentTypes.Add("application/problem+json");
            result.ContentTypes.Add("application/problem+xml");

            return result;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
