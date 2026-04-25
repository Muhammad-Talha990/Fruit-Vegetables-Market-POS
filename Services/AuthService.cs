using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using GroceryPOS.Data;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Data.Repositories; // Fixed namespace

namespace GroceryPOS.Services
{
    /// <summary>
    /// Authentication service using raw SQL against the User table.
    /// Handles login, logout, password changes, and user creation.
    /// </summary>
    public class AuthService
    {
        private readonly UserRepository _userRepo;
        private readonly DataCacheService _cache;

        public AuthService(UserRepository userRepo, DataCacheService cache)
        {
            _userRepo = userRepo;
            _cache = cache;
        }

        public User? CurrentUser { get; private set; }

        public bool Login(string username, string password)
        {
            try
            {
                // Use Cache for fast user lookup
                var user = _cache.GetUserByUsername(username);

                if (user == null)
                {
                    AppLogger.Warning($"Login attempt failed: user '{username}' not found in cache.");
                    return false;
                }

                // Defensive: ensure stored hash and provided password are valid before verifying
                if (string.IsNullOrWhiteSpace(user.PasswordHash))
                {
                    AppLogger.Warning($"Login attempt failed: user '{username}' has no password hash.");
                    return false;
                }

                if (password == null)
                {
                    AppLogger.Warning($"Login attempt failed: null password provided for '{username}'.");
                    return false;
                }

                if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                {
                    AppLogger.Warning($"Login attempt failed: incorrect password for '{username}'.");
                    return false;
                }

                CurrentUser = user;
                AppLogger.Info($"User '{username}' logged in successfully. Role: {user.Role}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Login error", ex);
                return false;
            }
        }

        public void Logout()
        {
            if (CurrentUser != null)
                AppLogger.Info($"User '{CurrentUser.Username}' logged out.");
            CurrentUser = null;
        }

        public bool IsAdmin => CurrentUser?.Role == "Admin";
        public bool IsCashier => CurrentUser?.Role == "Cashier";

        public bool ChangePassword(string currentPassword, string newPassword)
        {
            if (CurrentUser == null) return false;

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, CurrentUser.PasswordHash))
                return false;

            var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET PasswordHash = @hash WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@hash", newHash);
            cmd.Parameters.AddWithValue("@id", CurrentUser.Id);
            cmd.ExecuteNonQuery();

            CurrentUser.PasswordHash = newHash;
            AppLogger.Info($"Password changed for user '{CurrentUser.Username}'.");
            return true;
        }

        public void CreateUser(string username, string password, string fullName, string role)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Users (Username, PasswordHash, FullName, Role, IsActive, CreatedAt)
                VALUES (@user, @hash, @name, @role, 1, @created);
            ";
            cmd.Parameters.AddWithValue("@user", username);
            cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password));
            cmd.Parameters.AddWithValue("@name", fullName);
            cmd.Parameters.AddWithValue("@role", role);
            cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();

            // Refresh cache after DB successful write
            _cache.RefreshUserCache();
            AppLogger.Info($"New user created and cache refreshed: '{username}'.");
        }
    }
}
