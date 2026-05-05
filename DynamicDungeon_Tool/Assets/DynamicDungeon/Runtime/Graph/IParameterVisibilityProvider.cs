namespace DynamicDungeon.Runtime.Graph
{
    public interface IParameterVisibilityProvider
    {
        bool IsParameterVisible(string parameterName);
    }
}
