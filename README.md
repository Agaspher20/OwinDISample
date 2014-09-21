<h4>Введение</h4>
На данном ресурсе <a href="http://habrahabr.ru/search/?q=owin">множество раз </a> затрагивалась тема OWIN, однако до сих пор то и дело всплывают вопросы о реализации приложений и компонентов с помощью OWIN.
В данной статье я начну со стандартного шаблона Visual Studio 2013 и продемонстрирую реализацию архитектуры приложения. Также я покажу как использовать один DI-контейнер как для MVC, так и для WebApi в рамках одного проекта.
<habracut />
<h4>Конфигурирование WebApi</h4>
В стандартном шаблоне VS2013 конфигурация WebApi выполняется в global.asax. Перенесем ее в класс Startup.
Теперь зарегистрируем OWIN модуль WebApi. Для этого нам необходимо установить соответствующий NuGet пакет. Открываем Package Manager Console и вводим

<source lang="bash">
PM> Install-Package Microsoft.AspNet.WebApi.Owin
</source>

После установки пакета мы можем зарегистрировать OWIN Middleware для WebApi.

<source lang="cs">
var apiConfig = ConfigureWebApi();
ConfigureDependencyInjection(app, apiConfig);
app.UseWebApi(apiConfig);
</source>

О методе «ConfigureDependencyInjection» мы поговорим далее.

<h4>Конфигурирование DI контейнера</h4>
В данном примере я использую DI-контейнер Autofac, т.к. он уже располагает необходимыми реализациями классов DependencyResolver для WebApi и MVC, а также методами расширения для интеграции с OWIN.
Установим необходимые модули:

<source lang="bash">
PM> Install-Package Autofac.Mvc5
PM> Install-Package Autofac.WebApi2.Owin
</source>

Для интеграции MVC и Owin необходимо поставить еще один пакет:

<source lang="bash">
PM> Install-Package Autofac.Mvc5.Owin
</source>

Поскольку я хочу продемонстрировать использование единого контейнера для WebApi и MVC, инициализация контейнера будет расположена в конфигурационном классе OWIN.

<source lang="cs">
private void ConfigureDependencyInjection(IAppBuilder app, HttpConfiguration apiConfig)
{
    var builder = new ContainerBuilder();
    Assembly executingAssembly = Assembly.GetExecutingAssembly();
    builder.RegisterApiControllers(executingAssembly);
    builder.RegisterControllers(executingAssembly);
    RegisterComponents(builder);
    var container = builder.Build();
    app.UseAutofacMiddleware(container);
    var apiResolver = new AutofacWebApiDependencyResolver(container);
    apiConfig.DependencyResolver = apiResolver;
    app.UseAutofacWebApi(apiConfig);
    var mvcResolver = new AutofacDependencyResolver(container);
    DependencyResolver.SetResolver(mvcResolver);
    app.UseAutofacMvc();
}
</source>

С этим кодом все очень просто. Сначала мы создаем ContainerBuilder и регистрируем в нём наши контроллеры, затем регистрируем сервисы. После этого создаем контейнер и устанавливаем его как DependencyResolver для WebApi и Mvc.
Здесь необходимо обратить внимание на строчку app.UseAutofacMvc(); вызов этого метода позволяет расширить LifetimeScope объектов, чтобы они задействовались в MVC.

<h4>Реализация компонента безопасности приложения на примере AspNet Identity</h4>
<h5>Регистрирование компонентов</h5>
В стандартном шаблоне приложения уже установлены пакеты AspNet Identity, однако если вы начали с пустого шаблона, то необходимо установить следующие пакеты

<source lang="bash">
PM> Install-Package Microsoft.AspNet.Identity.Owin
PM> Install-Package Microsoft.AspNet.Identity.EntityFramework
</source>

Для реализации безопасности AspNet Identity нам необходимо зарегистрировать четыре класса:
<ul>
	<li>UserManager&lt;ApplicationUser&gt;</li>
        <li>SignInManager&lt;ApplicationUser, string&gt;</li>
        <li>IUserStore&lt;ApplicationUser&gt;</li>
        <li>IAuthenticationManager</li>
</ul>
С регистрацией компонентов SignInManager<ApplicationUser, string> и IUserStore<ApplicationUser> нет никаких проблем, код их регистрации приведен ниже.

<source lang="cs">
private void RegisterComponents(ContainerBuilder builder)
{
    builder.RegisterType<ApplicationDbContext>().As<DbContext>().InstancePerRequest();
    builder.RegisterType<ApplicationSignInManager>().As<SignInManager<ApplicationUser, string>>().InstancePerRequest();
    builder.RegisterType<UserStore<ApplicationUser>>().As<IUserStore<ApplicationUser>>().InstancePerRequest();
}
</source>

Стоит заметить, что в качестве IUserStore я использовал класс библиотеки AspNet.Identity.EntityFramework, поэтому в регистрации присутствует класс ApplicationDbContext.
Далее необходимо зарегистрировать IAuthenticationManager. Здесь необходимо обратить внимание, что имплементация интерфейса IAuthenticationManager не имеет открытого конструктора, поэтому задаем factory method.

<source lang="cs">
builder.Register<IAuthenticationManager>((c, p) => c.Resolve<IOwinContext>().Authentication).InstancePerRequest();
</source>

Свойство IOwinContext.Authentication фактически является методом-фабрикой и предоставляет нам новый AuthenticationManager при каждом вызове.
Теперь необходимо зарегистрировать класс UserManager. Конструктор этого класса не представляет особого интереса, но ниже в этом классе определен factory method “Create”, который отвечает за создание и конфигурацию этого класса.
Перенесем создание и конфигурирование класса в factory method autofac, чтобы держать всю конфигурацию вместе. В этом случае мы столкнемся с небольшой проблемой. Метод “Create” принимает IdentityFactoryOptions в качестве одного из аргументов. Мы не можем создать IdentityFactoryOptions сами. К счастью существует метод IAppBuilder.GetDataProtectionProvider(), расположенный в неймспейсе Microsoft.Owin.Security.DataProtection.

<source lang="cs">
var dataProtectionProvider = app.GetDataProtectionProvider();
builder.Register<UserManager<ApplicationUser>>((c, p) => BuildUserManager(c, p, dataProtectionProvider));
</source>

<h5>Бизнес логика</h5>
Теперь можно использовать наш DI-контейнер для реализации логики приложения.
Если мы посмотрим в AccountController, то увидим там такие строки

<source lang="cs">
HttpContext.GetOwinContext().Get<ApplicationSignInManager>();
HttpContext.GetOwinContext().GetUserManager<ApplicationUserManager>();
HttpContext.GetOwinContext().Authentication;
</source>

С помощью этих строк разрешаются объекты классов UserManager, SignInManager и IAuthenticationManager соответственно.
Такой подход предлагается библиотекой AspNet Identity. Он не подходит нам по нескольким причинам, самые очевидные из них:
<ol>
	<li>Использование ServiceLocator не позволяет нам контролировать зависимости внутри класса.</li>
        <li>Появление второго DI контейнера, который напрямую зависит от AspNet Identity.</li>
</ol>
Удалим свойства UserManager, SignInManager, AuthenticationManager и добавим инициализацию полей _userManager и _authenticationManager через конструктор. Также удалим конструктор без параметров.
Аналогичным образом исправим ManageController.
В методе конфигурации Identity убираем строки, регистрирующие наши классы в OwinContext.

<source lang="cs">
    app.CreatePerOwinContext(ApplicationDbContext.Create);
    app.CreatePerOwinContext<ApplicationUserManager>(ApplicationUserManager.Create);
    app.CreatePerOwinContext<ApplicationSignInManager>(ApplicationSignInManager.Create);
</source>

Теперь можно удалить лишние пакеты, отвечавшие за интеграцию WebApi c IIS.

<source lang="bash">
Uninstall-Package Microsoft.AspNet.WebApi
Uninstall-Package Microsoft.AspNet.WebApi.WebHost
</source>

<h4>Заключение</h4>
В данной статье мы узнали как реализовать модульную структуру ASP.Net приложения с регистрацией компонентов в качестве OWIN Middleware, зарегистрировали единый Dependency Injection контейнер для ASP.Net MVC и WebApi и реализовали с его помощью модуль безопасности приложения.
Полный код приложения доступен по <a href="https://github.com/Agaspher20/OwinDISample">ссылке на GitHub</a>.
