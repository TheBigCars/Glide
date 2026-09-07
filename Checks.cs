using System;
using System.IO;
using System.Linq;
using System.Text;

internal static class Checks
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static int Main(string[] args)
    {
        try
        {
            Assert(MouseDevice.Crc(Encoding.ASCII.GetBytes("123456789"), 9) == 0x29B1, "CRC reference vector failed");
            Assert(MouseDevice.DecodeFirmware(new byte[]{0,0,0,0,0x32,0x05,0,0x29})=="32.5.29","Firmware decoding failed");
            Assert(MouseDevice.DecodeFirmware(new byte[8])=="0.0.0","Zero firmware field failed");
            var release=FirmwareUpdates.Parse("PRO X SUPERLIGHT 2 DEX (Mouse ver: 37.9.99) PRO X SUPERLIGHT 2 (Mouse ver: 32.5.29) PRO X SUPERLIGHT 2 (Mouse ver. 32.4.27)","32.4.27");
            Assert(release.Published=="32.5.29" && release.Summary.Contains("Newer"),"Exact-model update parsing failed");
            try { FirmwareUpdates.Parse("PRO X SUPERLIGHT 2 SE (Mouse ver: 32.5.29)","32.5.29"); throw new Exception("Wrong-model firmware accepted"); } catch(InvalidDataException) { }
            int[] values = MouseDevice.DecodeValues(new byte[] { 0, 100, 0xE0, 1, 0, 200, 0xE0, 2, 1, 244, 0, 0 });
            Assert(values.First() == 100 && values.Last() == 500 && values.Contains(202) && !values.Contains(201), "DPI range decoding failed");
            try { MouseDevice.DecodeValues(new byte[] { 0,100,0xE0,0,0,200,0,0 }); throw new Exception("Zero step accepted"); } catch (InvalidDataException) { }
            var fixture = new MouseState { Dpi = 800, DpiY = 800, Lod = 2, Slot = 0, Values = new int[] { 800, 805 }, ProfileBytes = Enumerable.Repeat((byte)0xFF, 255).ToArray() };
            fixture.ProfileBytes[2] = 0; fixture.ProfileBytes[4] = fixture.ProfileBytes[6] = 0x20;
            fixture.ProfileBytes[5] = fixture.ProfileBytes[7] = 3; fixture.ProfileBytes[8] = 2;
            int checksum = MouseDevice.Crc(fixture.ProfileBytes, 253); fixture.ProfileBytes[253] = (byte)(checksum >> 8); fixture.ProfileBytes[254] = (byte)checksum;
            byte[] patch = MouseDevice.PatchProfile(fixture, 805);
            Assert(MouseDevice.LE(patch,4) == 805 && MouseDevice.LE(patch,6) == 805, "DPI patch failed");
            for(int i=0;i<253;i++) if(i<4 || i>7) Assert(patch[i] == fixture.ProfileBytes[i], "Unrelated profile byte changed");
            Assert(MouseDevice.Crc(patch,253) == MouseDevice.BE(patch,253), "Patched CRC failed");
            Assert(MouseDevice.LE(fixture.ProfileBytes,4) == 800, "Original profile mutated");
            fixture.ProfileBytes[90] ^= 1;
            try { MouseDevice.PatchProfile(fixture,805); throw new Exception("Corrupt profile accepted"); } catch(InvalidDataException) { }
            Console.WriteLine("PASS: CRC, variable DPI steps, malformed ranges, exact-byte preservation, corrupt-profile rejection.");
            if(args.Length > 0 && (args[0] == "--device" || args[0] == "--roundtrip" || args[0] == "--profiles-roundtrip"))
            {
                using(var mouse=MouseDevice.Connect())
                {
                    var state=mouse.Read();
                    Console.WriteLine("LIVE: " + state.Connection + ", " + state.Battery + "%, " + state.ChargeText + ", " + state.Dpi + "/" + state.DpiY + " DPI");
                    Console.WriteLine("Range: " + state.Values.First() + "–" + state.Values.Last() + "; " + state.Values.Length + " supported values.");
                    Console.WriteLine("Profile: " + state.Profile + "; slot: " + state.Slot + "; save supported: " + state.CanSave + "; " + state.SaveUnavailable);
                    Console.WriteLine("Installed firmware: " + state.Firmware + "; onboard profiles: " + string.Join(" / ", state.Profiles.Select(p=>p.ToString())));
                    if(args[0]=="--profiles-roundtrip")
                    {
                        Assert(state.CanSave,"Current profile cannot be safely edited");
                        var other=state.Profiles.First(p=>p.Sector!=state.Profile);
                        try
                        {
                            var selected=mouse.Activate(other.Sector,state);
                            Assert(selected.Profile==other.Sector,"Profile switch failed");
                            Console.WriteLine("PROFILE SELECT VERIFIED: "+selected.Profile+" / "+selected.Dpi+" DPI");
                        }
                        finally
                        {
                            var active=mouse.Read();
                            var restored=mouse.Activate(state.Profile,active);
                            Assert(restored.ProfileBytes.SequenceEqual(state.ProfileBytes),"Profile switch altered original profile bytes");
                            Console.WriteLine("PROFILE RESTORED: "+restored.Profile+" / "+restored.Dpi+" DPI");
                        }
                        try
                        {
                            var renamed=mouse.Save(state.Dpi,state,"Controller test");
                            Assert(renamed.ProfileName=="Controller test","Rename failed");
                            for(int i=0;i<253;i++)if(i<160 || i>=208)Assert(renamed.ProfileBytes[i]==state.ProfileBytes[i],"Rename altered unrelated byte");
                            Console.WriteLine("RENAME VERIFIED; unrelated bytes preserved.");
                        }
                        finally
                        {
                            var active=mouse.Read();
                            if(active.ProfileName=="Controller test")active=mouse.Save(state.Dpi,active,state.ProfileName);
                            Assert(active.ProfileBytes.SequenceEqual(state.ProfileBytes),"Original name/profile bytes not restored");
                            Console.WriteLine("NAME RESTORED; all 255 original profile bytes match.");
                        }
                    }
                    if(state.CanSave)
                    {
                        int candidate=state.Values.First(v=>v>state.Dpi);
                        byte[] planned=MouseDevice.PatchProfile(state,candidate);
                        Console.WriteLine("Read-only save rehearsal: " + state.Dpi + " -> " + candidate + "; bytes changed: " + string.Join(",",Enumerable.Range(0,255).Where(i=>planned[i]!=state.ProfileBytes[i])));
                        Assert(MouseDevice.Crc(planned,253)==MouseDevice.BE(planned,253),"Live profile patch CRC failed");
                        if(args[0] == "--roundtrip")
                        {
                            MouseState changed=null;
                            try
                            {
                                changed=mouse.Save(candidate,state);
                                Console.WriteLine("WRITE VERIFIED: " + changed.Dpi + " DPI; complete profile read-back matched.");
                            }
                            finally
                            {
                                var latest=mouse.Read();
                                if(latest.ProfileBytes.SequenceEqual(planned))
                                {
                                    var restored=mouse.Save(state.Dpi,latest);
                                    Assert(restored.ProfileBytes.SequenceEqual(state.ProfileBytes),"Original profile not restored exactly");
                                    Console.WriteLine("RESTORED: " + restored.Dpi + " DPI; all 255 original profile bytes match.");
                                }
                                else if(!latest.ProfileBytes.SequenceEqual(state.ProfileBytes))
                                    throw new Exception("Profile differs from both planned and original; original backup retained for recovery.");
                            }
                        }
                    }
                }
            }
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
