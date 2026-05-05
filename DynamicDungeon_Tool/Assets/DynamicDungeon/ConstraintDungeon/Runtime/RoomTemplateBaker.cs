namespace DynamicDungeon.ConstraintDungeon
{
    public static class RoomTemplateBaker
    {
#if UNITY_EDITOR
        public static void Bake(RoomTemplateComponent room)
        {
            if (room == null)
            {
                return;
            }

            room.Bake();
        }
#endif
    }
}
