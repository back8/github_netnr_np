using Microsoft.AspNetCore.Mvc;
using System;
using Netnr.Core;

namespace Netnr.Blog.Web.Controllers
{
    /// <summary>
    /// 混合、综合、其它
    /// </summary>
    public class MixController : Controller
    {
        /// <summary>
        /// 关于页面
        /// </summary>
        /// <returns></returns>
        public IActionResult About()
        {
            return View();
        }

        /// <summary>
        /// 服务器状态
        /// </summary>
        /// <returns></returns>
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 10)]
        public SharedResultVM AboutServerStatus()
        {
            var vm = new SharedResultVM();

            try
            {
                var ss = new SystemStatusTo();
                vm.Log.Add(ss);
                vm.Data = ss.ToView();
                vm.Set(SharedEnum.RTag.success);
            }
            catch (Exception ex)
            {
                vm.Set(ex);
                ConsoleTo.Log(ex);
            }

            return vm;
        }

        /// <summary>
        /// 条款
        /// </summary>
        /// <returns></returns>
        public IActionResult Terms()
        {
            return View();
        }

        /// <summary>
        /// FAQ
        /// </summary>
        /// <returns></returns>
        public IActionResult FAQ()
        {
            return View();
        }
    }
}