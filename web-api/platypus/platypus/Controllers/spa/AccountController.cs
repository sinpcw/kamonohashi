﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nssol.Platypus.ApiModels.AccountApiModels;
using Nssol.Platypus.Controllers.Util;
using Nssol.Platypus.DataAccess.Core;
using Nssol.Platypus.DataAccess.Repositories.Interfaces;
using Nssol.Platypus.Infrastructure;
using Nssol.Platypus.Infrastructure.Infos;
using Nssol.Platypus.Infrastructure.Options;
using Nssol.Platypus.Infrastructure.Types;
using Nssol.Platypus.Logic.Interfaces;
using Nssol.Platypus.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nssol.Platypus.Controllers.spa
{
    [Route("api/v1/account")]
    public class AccountController : PlatypusApiControllerBase
    {
        private readonly ILoginLogic loginLogic;
        private readonly IUserRepository userRepository;
        private readonly IMultiTenancyLogic multiTenancyLogic;
        private readonly IUnitOfWork unitOfWork;
        private readonly WebSecurityOptions webSecurityOptions;

        public AccountController(
            ILoginLogic loginLogic,
            IUserRepository userRepository,
            IMultiTenancyLogic multiTenancyLogic,
            IUnitOfWork unitOfWork,
            IOptions<WebSecurityOptions> webSecurityOptions,
            IHttpContextAccessor accessor) : base(accessor)
        {
            this.loginLogic = loginLogic;
            this.userRepository = userRepository;
            this.multiTenancyLogic = multiTenancyLogic;
            this.unitOfWork = unitOfWork;
            this.webSecurityOptions = webSecurityOptions.Value;
        }

        /// <summary>
        /// ログインユーザのアカウント情報を取得する
        /// </summary>
        [HttpGet]
        [Filters.PermissionFilter(MenuCode.Account)]
        [ProducesResponseType(typeof(AccountOutputModel), (int)HttpStatusCode.OK)]
        public IActionResult GetAccountInfo()
        {
            var userInfo = multiTenancyLogic.CurrentUserInfo;
            if (userInfo == null)
            {
                return JsonBadRequest("Couldn't get activated user informations");
            }

            AccountOutputModel model = new AccountOutputModel()
            {
                UserId = userInfo.Id,
                UserName = userInfo.Name,
                SelectedTenant = new TenantInfo(userInfo.SelectedTenant, userInfo.TenantDic, userInfo.DefaultTenant.Id),
                DefaultTenant = new TenantInfo(userInfo.DefaultTenant, userInfo.TenantDic, userInfo.DefaultTenant.Id),
                Tenants = userInfo.TenantDic.Select(x => new TenantInfo(x.Key, x.Value, userInfo.DefaultTenant.Id)).ToList(),
                PasswordChangeEnabled = userInfo.ServiceType == AuthServiceType.Local
            };

            return JsonOK(model);
        }

        /// <summary>
        /// ログインユーザのアカウント情報を変更する
        /// </summary>
        [HttpPut]
        [Filters.PermissionFilter(MenuCode.Account)]
        [ProducesResponseType(typeof(AccountOutputModel), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> EditAccountInfo(AccountInputModel model)
        {
            //入力値チェック
            if (!ModelState.IsValid)
            {
                return JsonBadRequest("Invalid inputs.");
            }

            var userInfo = multiTenancyLogic.CurrentUserInfo;
            if (userInfo == null)
            {
                return JsonBadRequest("Couldn't get activated user informations");
            }

            Tenant defaultTenant = userInfo.TenantDic.Keys.FirstOrDefault(t => t.Name == model.DefaultTenant);
            if (defaultTenant == null)
            {
                return JsonBadRequest("Invalid tenant name");
            }

            //アップデート処理
            User user = await userRepository.GetByIdAsync(userInfo.Id);
            user.DefaultTenantId = defaultTenant.Id;
            unitOfWork.Commit();

            AccountOutputModel result = new AccountOutputModel()
            {
                UserId = userInfo.Id,
                UserName = userInfo.Name,
                SelectedTenant = new TenantInfo(userInfo.SelectedTenant, userInfo.TenantDic, userInfo.DefaultTenant.Id),
                DefaultTenant = new TenantInfo(defaultTenant, userInfo.TenantDic, userInfo.DefaultTenant.Id),
                Tenants = userInfo.TenantDic.Select(x => new TenantInfo(x.Key, x.Value, userInfo.DefaultTenant.Id)).ToList()
            };

            return JsonOK(result);
        }


        /// <summary>
        /// ログインユーザのパスワードを変更する
        /// </summary>
        [HttpPut("password")]
        [Filters.PermissionFilter(MenuCode.Account)]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        public async Task<IActionResult> ChangePassword([FromBody] PasswordInputModel model, [FromServices] IUserRepository userRepository)
        {
            //データの存在チェック
            var user = await userRepository.GetByIdAsync(CurrentUserInfo.Id);
            if (user == null)
            {
                return JsonNotFound($"User ID {CurrentUserInfo.Id} is not found.");
            }
            //パスワード変更を許可するのは、ローカルアカウントのユーザのみ
            if(user.ServiceType != AuthServiceType.Local)
            {
                return JsonBadRequest($"Only local account user can change the password. Your account service type is {user.ServiceType} not Local.");
            }
            string oldHash = Infrastructure.Util.GenerateHash(model.CurrentPassword, user.Name);
            if(oldHash != user.Password)
            {
                return JsonBadRequest($"Password mismatch. Please input the current correct password.");
            }

            if (string.IsNullOrWhiteSpace(model.NewPassword))
            {
                //新しいパスワードに空文字は許可しない
                return JsonBadRequest($"New password is NOT allowed to set empty string.");
            }

            //パスワードをハッシュ化
            string hash = Infrastructure.Util.GenerateHash(model.NewPassword, user.Name);

            //もしパスワードが同じであれば、エラーにする
            if (oldHash == hash)
            {
                return JsonBadRequest($"Current password and new password are same.");
            }

            user.Password = hash;

            unitOfWork.Commit();

            return JsonNoContent();
        }

        /// <summary>
        /// ログインする
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        [Filters.PermissionFilter(MenuCode.Login)]
        [ProducesResponseType(typeof(LoginOutputModel), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> LoginAsync([FromBody] LoginInputModel model, [FromServices] ITenantRepository tenantRepository)
        {
            //入力値チェック
            if (!ModelState.IsValid)
            {
                return JsonBadRequest("Invalid inputs.");
            }

            //ユーザ情報からクレームを取得
            Result<List<Claim>, string> signInResult = await loginLogic.SignInAsync(model.UserName, model.Password, model.TenantId);
            if (!signInResult.IsSuccess)
            {
                //失敗
                return JsonBadRequest(signInResult.Error);
            }

            // 結果からトークンを作成
            var token = loginLogic.GenerateToken(signInResult.Value, model.ExpiresIn);

            //Tenant name must not be null. Hence "Single" is intended use here.
            // string tenantName = signInResult.Value.Single(c => c.Type == ApplicationConst.ClaimTypeTenantName).Value;
            long tenantId = long.Parse(signInResult.Value.FirstOrDefault(c => c.Type == ClaimTypes.GroupSid).Value);

            //テナント取得（ここまでに存在チェックは行われているハズ）
            var tenant = tenantRepository.Get(tenantId);

            var result = new LoginOutputModel()
            {
                Token = token.AccessToken,
                UserName = model.UserName,
                TenantId = tenantId,
                TenantName = tenant.DisplayName,
                ExpiresIn = token.ExpiresIn
            };

            return JsonOK(result);
        }

        /// <summary>
        /// 現在の認証情報を使用し、新規にアクセストークンを取得する
        /// </summary>
        /// <param name="tenantId">テナントID</param>
        /// <param name="expiresIn">有効期限(秒)。省略時はシステムの既定値。</param>
        /// <param name="tenantRepository">DI用</param>
        [HttpPost("tenants/{tenantId}/token")]
        [Filters.PermissionFilter(MenuCode.Account)]
        [ProducesResponseType(typeof(LoginOutputModel), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> SwitchTenantAsync([FromRoute] long tenantId, [FromServices] ITenantRepository tenantRepository, [FromQuery] int? expiresIn)
        {
            var tenant = tenantRepository.Get(tenantId);
            if (tenant == null)
            {
                return JsonNotFound($"Tenant ID {tenantId} is not found.");
            }

            Result<List<Claim>, string> authResult = await loginLogic.AuthorizeAsync(UserName, null, tenantId);
            
            // 結果からトークンを作成
            var token = loginLogic.GenerateToken(authResult.Value, expiresIn);

            var result = new LoginOutputModel()
            {
                Token = token.AccessToken,
                UserName = UserName,
                TenantId = tenantId,
                TenantName = tenant.DisplayName,
                ExpiresIn = token.ExpiresIn
            };

            return JsonOK(result);
        }


        /// <summary>
        /// アクセス可能なメニュー一覧をツリー形式で取得する。
        /// </summary>
        [HttpGet("menus/tree")]
        [ProducesResponseType(typeof(IEnumerable<MenuTreeOutputModel.MenuGroup>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetAccessibleMenuTree([FromQuery] string lang, [FromServices] IMenuLogic menuLogic)
        {
            var result = new List<MenuTreeOutputModel>();

            var menus = await menuLogic.GetSideMenuListAsync();
            foreach (var menu in menus)
            {
                result.Add(MenuTreeOutputModel.GenerateMenu(menu, lang));
            }
            return JsonOK(result);
        }


        /// <summary>
        /// アクセス可能なメニュー一覧をリスト形式で取得する。
        /// </summary>
        [HttpGet("menus/list")]
        [Filters.PermissionFilter(MenuCode.Account)]
        [ProducesResponseType(typeof(IEnumerable<MenuListOutputModel>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetAccessibleMenuList([FromQuery] string lang, [FromServices] IMenuLogic menuLogic)
        {
            var menus = await menuLogic.GetTopMenuListAsync();

            var result = menus.Select(m => new MenuListOutputModel(m, lang));

            return JsonOK(result);
        }

        /// <summary>
        /// 選択中のテナントにおけるGit情報を取得する
        /// </summary>
        /// <param name="gitRepository">DI用</param>
        [Filters.PermissionFilter(MenuCode.Account)]
        [HttpGet("gits")]
        [ProducesResponseType(typeof(GitInfoOutputModel), (int)HttpStatusCode.OK)]
        public IActionResult GetGitInfos([FromServices] IGitRepository gitRepository)
        {
            var tenant = CurrentUserInfo.SelectedTenant;
            var result = new GitInfoOutputModel()
            {
                DefaultGitId = tenant.DefaultGit?.Id
            };

            var gitMaps = gitRepository.GetUserTenantGitMapAll(tenant.Id, CurrentUserInfo.Id);
            result.Gits = gitMaps.Select(map => new GitCredentialOutputModel(map));
            return JsonOK(result);
        }

        /// <summary>
        /// 選択中のテナントにおけるGitのトークン情報を更新する。
        /// </summary>
        /// <param name="model">更新内容</param>
        /// <param name="gitRepository">DI用</param>
        [Filters.PermissionFilter(MenuCode.Account)]
        [HttpPut("gits")]
        [ProducesResponseType(typeof(GitCredentialOutputModel), (int)HttpStatusCode.OK)]
        public IActionResult EditGitToken([FromBody] GitCredentialInputModel model,
            [FromServices] IGitRepository gitRepository)
        {
            //入力値チェック
            if (!ModelState.IsValid)
            {
                return JsonBadRequest("Invalid inputs.");
            }

            var tenantId = CurrentUserInfo.SelectedTenant.Id;

            //まずは今の情報を取得
            var userGitMap = gitRepository.GetUserTenantGitMap(CurrentUserInfo.Id, tenantId, model.Id);

            if (userGitMap == null)
            {
                //今の値がないのはオカシイ。紐づけられていない情報を変更しようとしているとみなす。
                return JsonBadRequest("Couldn't map the git to the current user & tenant. Please contact a user administrator.");
            }

            //変更
            userGitMap.GitToken = model.Token;

            unitOfWork.Commit();

            GitCredentialOutputModel result = new GitCredentialOutputModel(userGitMap);

            return JsonOK(result);
        }

        /// <summary>
        /// 選択中のテナントにおけるレジストリ情報を取得する
        /// </summary>
        /// <param name="registryRepository">DI用</param>
        [Filters.PermissionFilter(MenuCode.Account)]
        [HttpGet("registries")]
        [ProducesResponseType(typeof(RegistryInfoOutputModel), (int)HttpStatusCode.OK)]
        public IActionResult GetRegistryInfos([FromServices] IRegistryRepository registryRepository)
        {
            var tenant = CurrentUserInfo.SelectedTenant;
            var result = new RegistryInfoOutputModel()
            {
                DefaultRegistryId = tenant.DefaultRegistry?.Id
            };

            var registryMaps = registryRepository.GetUserTenantRegistryMapAll(tenant.Id, CurrentUserInfo.Id);
            result.Registries = registryMaps.Select(map => new RegistryCredentialOutputModel(map));
            return JsonOK(result);
        }

        /// <summary>
        /// 選択中のテナントにおけるレジストリのトークン情報を更新する。
        /// </summary>
        /// <param name="model">更新内容</param>
        /// <param name="registryRepository">DI用</param>
        /// <param name="clusterManagementLogic">DI用</param>
        [Filters.PermissionFilter(MenuCode.Account)]
        [HttpPut("registries")]
        [ProducesResponseType(typeof(RegistryCredentialOutputModel), (int)HttpStatusCode.OK)]
        public IActionResult EditRegistryToken([FromBody] RegistryCredentialInputModel model,
            [FromServices] IRegistryRepository registryRepository,
            [FromServices] IClusterManagementLogic clusterManagementLogic)
        {
            //入力値チェック
            if (!ModelState.IsValid)
            {
                return JsonBadRequest("Invalid inputs.");
            }

            var tenantId = CurrentUserInfo.SelectedTenant.Id;

            //まずは今の情報を取得
            var userRegistryMap = registryRepository.GetUserTenantRegistryMap(CurrentUserInfo.Id, tenantId, model.Id);

            if (userRegistryMap == null)
            {
                //今の値がない場合、紐づけられていない情報を変更しようとしているとみなす。
                return JsonBadRequest("Couldn't map the registry to the current user & tenant. Please contact a user administrator.");
            }

            //変更
            userRegistryMap.RegistryUserName = model.UserName;
            userRegistryMap.RegistryPassword = model.Password;

            //シークレットを登録する。
            clusterManagementLogic.RegistRegistryToTenantAsync(CurrentUserInfo.SelectedTenant.Name, userRegistryMap);

            unitOfWork.Commit();

            RegistryCredentialOutputModel result = new RegistryCredentialOutputModel(userRegistryMap);

            return JsonOK(result);
        }
    }
}
