using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Newtonsoft.Json.Linq;
using Netnr.Login;
using Netnr.SharedFast;
using Netnr.SharedLogging;

namespace Netnr.Blog.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IHostEnvironment env)
        {
            GlobalTo.Configuration = configuration;
            GlobalTo.HostEnvironment = env;

            //设置日志
            LoggingTo.OptionsDbRoot = GlobalTo.GetValue("logs:path").Replace("~", GlobalTo.ContentRootPath);
            LoggingTo.OptionsCacheWriteCount = GlobalTo.GetValue<int>("logs:CacheWriteCount");
            LoggingTo.OptionsCacheWriteSecond = GlobalTo.GetValue<int>("logs:CacheWriteSecond");

            #region 第三方登录
            new List<Type>
            {
                typeof(QQConfig),
                typeof(WeChatConfig),
                typeof(WeiboConfig),
                typeof(GitHubConfig),
                typeof(GiteeConfig),
                typeof(TaoBaoConfig),
                typeof(MicroSoftConfig),
                typeof(DingTalkConfig),
                typeof(GoogleConfig),
                typeof(AliPayConfig),
                typeof(StackOverflowConfig)
            }.ForEach(lc =>
            {
                var fields = lc.GetFields();
                foreach (var field in fields)
                {
                    if (!field.Name.StartsWith("API_"))
                    {
                        var cv = GlobalTo.GetValue($"OAuthLogin:{lc.Name.Replace("Config", "")}:{field.Name}");
                        field.SetValue(lc, cv);
                    }
                }
            });
            #endregion
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                //cookie存储需用户同意，欧盟新标准，暂且关闭，否则用户没同意无法写入
                options.CheckConsentNeeded = context => false;

                if (!GlobalTo.GetValue<bool>("ReadOnly"))
                {
                    //允许其他站点携带授权Cookie访问，会出现伪造
                    //Chrome新版本必须启用HTTPS，安装命令：dotnet dev-certs https
                    options.MinimumSameSitePolicy = SameSiteMode.None;
                }
            });

            IMvcBuilder builder = services.AddControllersWithViews(options =>
            {
                //注册全局错误过滤器
                options.Filters.Add(new Apps.FilterConfigs.ErrorActionFilter());

                //注册全局过滤器
                options.Filters.Add(new Apps.FilterConfigs.GlobalFilter());

                //注册全局授权访问时登录标记是否有效
                options.Filters.Add(new Apps.FilterConfigs.LoginSignValid());
            });

#if DEBUG
            builder.AddRazorRuntimeCompilation();
#endif

            services.AddControllers().AddNewtonsoftJson(options =>
            {
                //Action原样输出JSON
                options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
                //日期格式化
                options.SerializerSettings.DateFormatString = "yyyy-MM-dd HH:mm:ss.fff";
            });

            //配置swagger
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = $"{GlobalTo.GetValue("Common:EnglishName")} API",
                    Description = string.Join(" &nbsp; ", new List<string>
                    {
                        "<b>Source</b>：<a target='_blank' href='https://github.com/netnr/np'>https://github.com/netnr/np</a>",
                        "<b>Blog</b>：<a target='_blank' href='https://www.netnr.com'>https://www.netnr.com</a>"
                    })
                });

                "Blog.Web,Blog.Application".Split(',').ToList().ForEach(x =>
                {
                    c.IncludeXmlComments(AppContext.BaseDirectory + $"Netnr.{x}.xml", true);
                });
            });

            //授权访问信息
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
            {
                if (!GlobalTo.GetValue<bool>("ReadOnly"))
                {
                    //允许其他站点携带授权Cookie访问，会出现伪造
                    //Chrome新版本必须启用HTTPS，安装命令：dotnet dev-certs https
                    options.Cookie.SameSite = SameSiteMode.None;
                }

                options.Cookie.Name = "netnr_auth";
                options.LoginPath = "/account/login";
            });

            //session
            services.AddSession();

            //数据库连接池
            services.AddDbContextPool<Data.ContextBase>(options =>
            {
                Data.ContextBaseFactory.CreateDbContextOptionsBuilder(options);
            }, 20);

            //定时任务
            if (!GlobalTo.GetValue<bool>("ReadOnly"))
            {
                FluentScheduler.JobManager.Initialize(new Application.TaskService.TaskComponent.Reg());
            }

            //配置上传文件大小限制（详细信息：FormOptions）
            services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = GlobalTo.GetValue<int>("StaticResource:MaxSize") * 1024 * 1024;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, Data.ContextBase db)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // The default HSTS value is 30 days. https://aka.ms/aspnetcore-hsts
                app.UseHsts();
            }

            //数据库不存在则创建，创建后返回true
            if (db.Database.EnsureCreated())
            {
                //从JSON写入数据库
                var jodb = Core.FileTo.ReadText(GlobalTo.ContentRootPath + "/db/data.json").ToJObject();

                var dicDbSet = Data.ContextBase.GetDicDbSet(db);
                foreach (var table in dicDbSet.Keys)
                {
                    var dbset = dicDbSet[table];
                    var gt = dbset.GetType();
                    var ttype = Type.GetType(gt.FullName.Split("[[")[1].TrimEnd(']'));

                    var jarows = jodb[table] as JArray;
                    for (int i = 0; i < jarows?.Count; i++)
                    {
                        var mo = jarows[i].ToJson().ToType(ttype);
                        gt.GetMethod("Add").Invoke(dbset, new object[] { mo });
                    }
                }

                var num = db.SaveChanges();
                Console.WriteLine($"Initialize the database, write {num} rows of data");
            }

            //配置swagger
            app.UseSwagger().UseSwaggerUI(c =>
            {
                c.DocumentTitle = $"{GlobalTo.GetValue("Common:EnglishName")} API";
                c.SwaggerEndpoint("/swagger/v1/swagger.json", c.DocumentTitle);
                c.InjectStylesheet("/Home/SwaggerCustomStyle");
            });

            //默认起始页index.html
            DefaultFilesOptions options = new DefaultFilesOptions();
            options.DefaultFileNames.Add("index.html");
            app.UseDefaultFiles(options);

            //静态资源允许跨域
            app.UseStaticFiles(new StaticFileOptions()
            {
                OnPrepareResponse = (x) =>
                {
                    x.Context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                }
            });

            app.UseRouting();

            //授权访问
            app.UseAuthentication();
            app.UseAuthorization();

            //session
            app.UseSession();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

                endpoints.MapControllerRoute("U", "{controller=U}/{id}", new { action = "index" });
                endpoints.MapControllerRoute("areas", "{area:exists}/{controller=Home}/{action=Index}/{id?}");
                endpoints.MapControllerRoute("Code", "{area:exists}/{controller=Code}/{id?}/{sid?}", new { action = "index" });
                endpoints.MapControllerRoute("Raw", "{area:exists}/{controller=Raw}/{id?}", new { action = "index" });
                endpoints.MapControllerRoute("User", "{area:exists}/{controller=User}/{id?}", new { action = "index" });
            });
        }
    }
}
