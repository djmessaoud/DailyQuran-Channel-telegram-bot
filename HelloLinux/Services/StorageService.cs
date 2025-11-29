using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using HelloLinux.Models;

namespace HelloLinux.Services
{
    public class StorageService
    {
        private readonly string _filePath;
        private List<GroupConfig> _groups;

        public StorageService()
        {
            string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _filePath = Path.Combine(dataDir, "groups.json");
            _groups = LoadGroups();
        }

        private List<GroupConfig> LoadGroups()
        {
            if (!File.Exists(_filePath))
            {
                return new List<GroupConfig>();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<GroupConfig>>(json) ?? new List<GroupConfig>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading groups: {ex.Message}");
                return new List<GroupConfig>();
            }
        }

        public void SaveGroups()
        {
            try
            {
                var json = JsonSerializer.Serialize(_groups, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving groups: {ex.Message}");
            }
        }

        public List<GroupConfig> GetGroups()
        {
            return _groups;
        }

        public GroupConfig GetGroup(long chatId)
        {
            var group = _groups.Find(g => g.ChatId == chatId);
            if (group == null)
            {
                group = new GroupConfig { ChatId = chatId };
                _groups.Add(group);
                SaveGroups();
            }
            return group;
        }

        public void UpdateGroup(GroupConfig group)
        {
            var index = _groups.FindIndex(g => g.ChatId == group.ChatId);
            if (index != -1)
            {
                _groups[index] = group;
                SaveGroups();
            }
        }
    }
}
