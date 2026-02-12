using ImageDetection.Models;

namespace ImageDetection.Services;

public partial class ImageConfigService
{
    /// <summary>
    /// Gets all modules.
    /// </summary>
    public IReadOnlyDictionary<string, DetectionModule> Modules => _state.Modules;

    /// <summary>
    /// Gets a module by ID.
    /// </summary>
    public DetectionModule? GetModule(string moduleId)
    {
        lock (_lock)
        {
            return _state.Modules.TryGetValue(moduleId, out var module) ? module : null;
        }
    }

    /// <summary>
    /// Creates a new module.
    /// </summary>
    public DetectionModule CreateModule(string name, string? description = null)
    {
        lock (_lock)
        {
            var id = SanitizeId(name);
            var originalId = id;
            var counter = 1;

            while (_state.Modules.ContainsKey(id))
            {
                id = $"{originalId}-{counter++}";
            }

            var module = new DetectionModule
            {
                Id = id,
                Name = name,
                Description = description,
                Enabled = true
            };

            var modulePath = Path.Combine(_modulesPath, id);
            Directory.CreateDirectory(modulePath);

            _state.Modules[id] = module;
            SaveConfig();

            return module;
        }
    }

    /// <summary>
    /// Updates a module's settings.
    /// </summary>
    public void UpdateModule(DetectionModule module)
    {
        lock (_lock)
        {
            if (!_state.Modules.ContainsKey(module.Id))
            {
                throw new InvalidOperationException($"Module not found: {module.Id}");
            }

            module.ModifiedAt = DateTime.UtcNow;
            _state.Modules[module.Id] = module;
            SaveConfig();
        }
    }

    /// <summary>
    /// Deletes a module and all its images.
    /// </summary>
    public void DeleteModule(string moduleId)
    {
        lock (_lock)
        {
            if (!_state.Modules.Remove(moduleId))
            {
                return;
            }

            var modulePath = Path.Combine(_modulesPath, moduleId);
            if (Directory.Exists(modulePath))
            {
                Directory.Delete(modulePath, true);
            }

            SaveConfig();
        }
    }

    /// <summary>
    /// Sets a module's enabled state.
    /// </summary>
    public void SetModuleEnabled(string moduleId, bool enabled)
    {
        lock (_lock)
        {
            if (_state.Modules.TryGetValue(moduleId, out var module))
            {
                module.Enabled = enabled;
                module.ModifiedAt = DateTime.UtcNow;
                SaveConfig();
            }
        }
    }
}
