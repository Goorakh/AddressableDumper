using RoR2;

namespace AddressableDumper
{
    static class MiscCommands
    {
        [ConCommand(commandName = "full_dump")]
        static void CCFullDump()
        {
            Console.instance.SubmitCmd(null, "refresh_addressables_key_cache; dump_addressable_values; dump_scenes");
        }
    }
}
