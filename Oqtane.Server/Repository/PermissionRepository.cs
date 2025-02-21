using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Oqtane.Extensions;
using Oqtane.Models;
using Microsoft.Extensions.Caching.Memory;
using Oqtane.Infrastructure;

namespace Oqtane.Repository
{
    public class PermissionRepository : IPermissionRepository
    {
        private TenantDBContext _db;
        private readonly IRoleRepository _roles;
        private readonly IMemoryCache _cache;
        private readonly SiteState _siteState;

        public PermissionRepository(TenantDBContext context, IRoleRepository roles, IMemoryCache cache, SiteState siteState)
        {
            _db = context;
            _roles = roles;
            _cache = cache;
            _siteState = siteState;
         }

        public IEnumerable<Permission> GetPermissions(int siteId, string entityName)
        {
            var alias = _siteState?.Alias;
            if (alias != null && alias.SiteId != -1)
            {
                return _cache.GetOrCreate($"permissions:{alias.SiteKey}:{entityName}", entry =>
                {
                    entry.SlidingExpiration = TimeSpan.FromMinutes(30);
                    return _db.Permission.Where(item => item.SiteId == alias.SiteId)
                        .Where(item => item.EntityName == entityName)
                        .Include(item => item.Role).ToList(); // eager load roles
                });
            }
            else
            {
                return _db.Permission.Where(item => item.SiteId == siteId || siteId == -1)
                    .Where(item => item.EntityName == entityName)
                    .Include(item => item.Role).ToList(); // eager load roles
            }
        }

        public IEnumerable<Permission> GetPermissions(string entityName, int entityId)
        {
            var permissions = GetPermissions(-1, entityName);
            return permissions.Where(item => item.EntityId == entityId);
        }

        public IEnumerable<Permission> GetPermissions(string entityName, int entityId, string permissionName)
        {
            var permissions = GetPermissions(-1, entityName);
            return permissions.Where(item => item.EntityId == entityId)
                .Where(item => item.PermissionName == permissionName);
        }

        public string GetPermissionString(int siteId, string entityName)
        {
            return GetPermissions(siteId, entityName)?.EncodePermissions();
        }

        public string GetPermissionString(string entityName, int entityId)
        {
            return GetPermissions(entityName, entityId)?.EncodePermissions();
        }

        public string GetPermissionString(string entityName, int entityId, string permissionName)
        {
            return GetPermissions(entityName, entityId, permissionName)?.EncodePermissions();
        }


        public Permission AddPermission(Permission permission)
        {
            _db.Permission.Add(permission);
            _db.SaveChanges();
            ClearCache(permission.EntityName);
            return permission;
        }

        public Permission UpdatePermission(Permission permission)
        {
            _db.Entry(permission).State = EntityState.Modified;
            _db.SaveChanges();
            ClearCache(permission.EntityName);
            return permission;
        }

        public void UpdatePermissions(int siteId, string entityName, int entityId, string permissionStrings)
        {
            // get current permissions and delete
            IEnumerable<Permission> permissions = _db.Permission
                .Where(item => item.EntityName == entityName)
                .Where(item => item.EntityId == entityId)
                .Where(item => item.SiteId == siteId);
            foreach (Permission permission in permissions)
            {
                _db.Permission.Remove(permission);
            }
            // add permissions
            permissions = DecodePermissions(permissionStrings, siteId, entityName, entityId);
            foreach (Permission permission in permissions)
            {
                _db.Permission.Add(permission);
            }
            _db.SaveChanges();
            ClearCache(entityName);
        }

        public Permission GetPermission(int permissionId)
        {
            return _db.Permission.Find(permissionId);
        }

        public void DeletePermission(int permissionId)
        {
            Permission permission = _db.Permission.Find(permissionId);
            _db.Permission.Remove(permission);
            _db.SaveChanges();
            ClearCache(permission.EntityName);
        }

        public void DeletePermissions(int siteId, string entityName, int entityId)
        {
            IEnumerable<Permission> permissions = _db.Permission
                .Where(item => item.EntityName == entityName)
                .Where(item => item.EntityId == entityId)
                .Where(item => item.SiteId == siteId);
            foreach (Permission permission in permissions)
            {
                _db.Permission.Remove(permission);
            }
            _db.SaveChanges();
            ClearCache(entityName);
        }

        private void ClearCache(string entityName)
        {
            var alias = _siteState?.Alias;
            if (alias != null && alias.SiteId != -1)
            {
                _cache.Remove($"permissions:{alias.SiteKey}:{entityName}");
            }
        }

        // permissions are stored in the format "{permissionname:!rolename1;![userid1];rolename2;rolename3;[userid2];[userid3]}" where "!" designates Deny permissions
        public string EncodePermissions(IEnumerable<Permission> permissionList)
        {
            List<PermissionString> permissionstrings = new List<PermissionString>();
            string permissionname = "";
            string permissions = "";
            StringBuilder permissionsbuilder = new StringBuilder();
            string securityid = "";
            foreach (Permission permission in permissionList.OrderBy(item => item.PermissionName))
            {
                // permission collections are grouped by permissionname
                if (permissionname != permission.PermissionName)
                {
                    permissions = permissionsbuilder.ToString();
                    if (permissions != "")
                    {
                        permissionstrings.Add(new PermissionString { PermissionName = permissionname, Permissions = permissions.Substring(0, permissions.Length - 1) });
                    }
                    permissionname = permission.PermissionName;
                    permissionsbuilder = new StringBuilder();
                }

                // deny permissions are prefixed with a "!"
                string prefix = !permission.IsAuthorized ? "!" : "";

                // encode permission
                if (permission.UserId == null)
                {
                    securityid = prefix + permission.Role.Name + ";";
                }
                else
                {
                    securityid = prefix + "[" + permission.UserId + "];";
                }

                // insert deny permissions at the beginning and append grant permissions at the end
                if (prefix == "!")
                {
                    permissionsbuilder.Insert(0, securityid);
                }
                else
                {
                    permissionsbuilder.Append(securityid);
                }
            }

            permissions = permissionsbuilder.ToString();
            if (permissions != "")
            {
                permissionstrings.Add(new PermissionString { PermissionName = permissionname, Permissions = permissions.Substring(0, permissions.Length - 1) });
            }
            return JsonSerializer.Serialize(permissionstrings);
        }

        public IEnumerable<Permission> DecodePermissions(string permissionStrings, int siteId, string entityName, int entityId)
        {
            List<Permission> permissions = new List<Permission>();
            List<Role> roles = _roles.GetRoles(siteId, true).ToList();
            string securityid = "";
            foreach (PermissionString permissionstring in JsonSerializer.Deserialize<List<PermissionString>>(permissionStrings))
            {
                foreach (string id in permissionstring.Permissions.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    securityid = id;
                    Permission permission = new Permission();
                    permission.SiteId = siteId;
                    permission.EntityName = entityName;
                    permission.EntityId = entityId;
                    permission.PermissionName = permissionstring.PermissionName;
                    permission.RoleId = null;
                    permission.UserId = null;
                    permission.IsAuthorized = true;

                    if (securityid.StartsWith("!"))
                    {
                        // deny permission
                        securityid = securityid.Replace("!", "");
                        permission.IsAuthorized = false;
                    }
                    if (securityid.StartsWith("[") && securityid.EndsWith("]"))
                    {
                        // user id
                        securityid = securityid.Replace("[", "").Replace("]", "");
                        permission.UserId = int.Parse(securityid);
                    }
                    else
                    {
                        // role name
                        Role role = roles.SingleOrDefault(item => item.Name == securityid);
                        if (role != null)
                        {
                            permission.RoleId = role.RoleId;
                        }
                    }
                    if (permission.UserId != null || permission.RoleId != null)
                    {
                        permissions.Add(permission);
                    }
                }
            }
            return permissions;
        }
    }
}
