.assembly extern mscorlib {}
.assembly extern AirHostQA {}
.assembly SdkExports {}
.module AirFixture.dll
.corflags 0
.class public abstract sealed FixtureExports {
 .method public static native int Start(native int name, uint32 raop, uint32 air, native int callback) cil managed {
  .export [1] as '?fgServerStart@@YAPEAXQEBDIIPEAVIAirServerCallback@@@Z'
  .maxstack 4
  ldarg.0
  ldarg.1
  ldarg.2
  ldarg.3
  call native int [AirHostQA]FixtureSdk::Start(native int,uint32,uint32,native int)
  ret
 }
 .method public static void Stop(native int server) cil managed {
  .export [2] as '?fgServerStop@@YAXPEAX@Z'
  .maxstack 1
  ldarg.0
  call void [AirHostQA]FixtureSdk::Stop(native int)
  ret
 }
}
