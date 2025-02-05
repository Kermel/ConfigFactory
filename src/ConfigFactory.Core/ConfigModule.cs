﻿using CommunityToolkit.Mvvm.ComponentModel;
using ConfigFactory.Core.Components;
using ConfigFactory.Core.Models;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigFactory.Core;

public abstract class ConfigModule<T> : ObservableObject, IConfigModule where T : ConfigModule<T>, new()
{
    protected virtual string SuccessColor { get; } = "#FF31C059";
    protected virtual string FailureColor { get; } = "#FFE64032";

    IConfigModule IConfigModule.Shared {
        get => Shared;
        set => Shared = (T)value;
    }

    public static T Shared { get; set; } = DefaultLoad();

    [JsonIgnore]
    public IValidationInterface? ValidationInterface { get; set; }

    /// <summary>
    /// The name of the <see cref="ConfigModule{T}"/>
    /// <para><i>Default: <see langword="typeof(T).Name"/></i></para>
    /// </summary>
    [JsonIgnore]
    public virtual string Name { get; }

    /// <summary>
    /// The local path of the serialized config file
    /// <para><i>Default: %localappdata%/<see langword="typeof(T).Name"/>/Config.json</i></para>
    /// </summary>
    [JsonIgnore]
    public virtual string LocalPath { get; }

    [JsonIgnore]
    public ConfigProperties Properties { get; }

    [JsonIgnore]
    public Dictionary<string, (Func<object?, bool>, string?)> Validators { get; } = new();

    public ConfigModule()
    {
        Name = typeof(T).Name;
        LocalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name, "Config.json");
        Properties = ConfigProperties.Generate<T>();
    }

    public virtual IConfigModule Load() => DefaultLoad();
    private static T DefaultLoad()
    {
        T config = new();

        if (!File.Exists(config.LocalPath)) {
            config.Save();
            return config;
        }

        using FileStream fs = File.OpenRead(config.LocalPath);
        config = JsonSerializer.Deserialize<T>(fs)!;

        foreach (var (name, (property, _)) in config.Properties) {
            typeof(T).GetMethod($"On{name}Changed", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(config, new[] {
                property.GetValue(config)
            });
        }

        return config;
    }

    public virtual void Reset()
    {
        IConfigModule config = Load();
        foreach (var (name, (property, _)) in Properties) {
            property.SetValue(Shared, config.Properties[name].Property.GetValue(config));
        }
    }

    protected virtual void Validate<TProperty>(Expression<Func<TProperty>> property, Func<TProperty?, bool> validation,
        string? invalidErrorMessage = null, string? validationFailureColor = null, string? validationSuccessColor = null)
    {
        PropertyInfo propertyInfo = (PropertyInfo)((MemberExpression)property.Body).Member;

        Validators.TryAdd(propertyInfo.Name,
            (x => validation((TProperty?)x), invalidErrorMessage));

        ValidationInterface?.SetValidationColor(propertyInfo,
            validation(property.Compile()()) ? validationSuccessColor ?? SuccessColor : validationFailureColor ?? FailureColor);
    }

    /// <summary>
    /// Executed before saving; return <see langword="false"/> to stop saving
    /// </summary>
    public event Func<bool> OnSaving = () => true;

    /// <summary>
    /// Executed after saving
    /// </summary>
    public event Action OnSave = () => { };

    public virtual void Save()
    {
        if (OnSaving()) {
            Directory.CreateDirectory(Path.GetDirectoryName(LocalPath)!);
            using FileStream fs = File.Create(LocalPath);
            JsonSerializer.Serialize(fs, (T)this);
            OnSave();
        }
    }

    public virtual bool Validate(out string? message, out ConfigProperty target)
    {
        foreach (var (name, (validate, errorMessage)) in Validators) {
            target = Properties[name];
            PropertyInfo propertyInfo = target.Property;
            if (validate(propertyInfo.GetValue(this)) is bool isValid) {
                ValidationInterface?.SetValidationColor(propertyInfo, isValid ? SuccessColor : FailureColor);

                if (!isValid) {
                    message = errorMessage;
                    return false;
                }
            }
        }

        target = new();
        message = "Validation Successful";
        return true;
    }

    /// <summary>
    /// Default implementation of the <see cref="IConfigModule.Translate(string)"/>.
    /// <br/>Returns the <paramref name="input"/> string.
    /// </summary>
    /// <param name="input">Input string (key)</param>
    /// <returns>Translated string</returns>
    public virtual string Translate(string input)
    {
        return input;
    }
}
