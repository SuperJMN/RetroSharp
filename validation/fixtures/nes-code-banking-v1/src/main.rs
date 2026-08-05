import RetroSharp.Portable2D;

// The fold stream below is generated bulk: 260 distinct branch folds over a running
// u16 mixer. Every line carries its own constants, so no shared body can absorb it and the
// canary keeps the program size it needs no matter how the target outlines user functions.
// Final state after the stream: mixer == 12254, counter.value == 171.

class Counter
{
    u8 value;
}

void Main()
{
    World.Load("assets/tiny.tmx");
    Camera.Init(32, 0, 20);
    Camera.Apply();
    Music.Asset(theme, "../../../samples/shared/platformer-assets/music/runner.vgz");
    Audio.Init();
    Music.Play(theme);
    Audio.Update();

    Counter counter;
    counter.value = 0;
    u16 mixer;
    mixer = 1;

    if (mixer > 1000) { mixer = mixer - 200; counter.value += 1; } else { mixer = mixer + 300; }
    if (mixer > 1037) { mixer = mixer - 253; counter.value += 1; } else { mixer = mixer + 329; }
    if (mixer > 1074) { mixer = mixer - 306; counter.value += 1; } else { mixer = mixer + 358; }
    if (mixer > 1111) { mixer = mixer - 359; counter.value += 1; } else { mixer = mixer + 387; }
    if (mixer > 1148) { mixer = mixer - 412; counter.value += 1; } else { mixer = mixer + 416; }
    if (mixer > 1185) { mixer = mixer - 465; counter.value += 1; } else { mixer = mixer + 445; }
    if (mixer > 1222) { mixer = mixer - 518; counter.value += 1; } else { mixer = mixer + 474; }
    if (mixer > 1259) { mixer = mixer - 571; counter.value += 1; } else { mixer = mixer + 503; }
    if (mixer > 1296) { mixer = mixer - 624; counter.value += 1; } else { mixer = mixer + 532; }
    if (mixer > 1333) { mixer = mixer - 677; counter.value += 1; } else { mixer = mixer + 561; }
    if (mixer > 1370) { mixer = mixer - 730; counter.value += 1; } else { mixer = mixer + 590; }
    if (mixer > 1407) { mixer = mixer - 783; counter.value += 1; } else { mixer = mixer + 619; }
    if (mixer > 1444) { mixer = mixer - 836; counter.value += 1; } else { mixer = mixer + 648; }
    if (mixer > 1481) { mixer = mixer - 889; counter.value += 1; } else { mixer = mixer + 677; }
    if (mixer > 1518) { mixer = mixer - 942; counter.value += 1; } else { mixer = mixer + 706; }
    if (mixer > 1555) { mixer = mixer - 995; counter.value += 1; } else { mixer = mixer + 735; }
    if (mixer > 1592) { mixer = mixer - 1048; counter.value += 1; } else { mixer = mixer + 764; }
    if (mixer > 1629) { mixer = mixer - 1101; counter.value += 1; } else { mixer = mixer + 793; }
    if (mixer > 1666) { mixer = mixer - 1154; counter.value += 1; } else { mixer = mixer + 822; }
    if (mixer > 1703) { mixer = mixer - 1207; counter.value += 1; } else { mixer = mixer + 851; }
    if (mixer > 1740) { mixer = mixer - 1260; counter.value += 1; } else { mixer = mixer + 880; }
    if (mixer > 1777) { mixer = mixer - 1313; counter.value += 1; } else { mixer = mixer + 909; }
    if (mixer > 1814) { mixer = mixer - 1366; counter.value += 1; } else { mixer = mixer + 938; }
    if (mixer > 1851) { mixer = mixer - 1419; counter.value += 1; } else { mixer = mixer + 967; }
    if (mixer > 1888) { mixer = mixer - 1472; counter.value += 1; } else { mixer = mixer + 996; }
    if (mixer > 1925) { mixer = mixer - 1525; counter.value += 1; } else { mixer = mixer + 1025; }
    if (mixer > 1962) { mixer = mixer - 1578; counter.value += 1; } else { mixer = mixer + 1054; }
    if (mixer > 1999) { mixer = mixer - 1631; counter.value += 1; } else { mixer = mixer + 1083; }
    if (mixer > 2036) { mixer = mixer - 1684; counter.value += 1; } else { mixer = mixer + 1112; }
    if (mixer > 2073) { mixer = mixer - 1737; counter.value += 1; } else { mixer = mixer + 1141; }
    if (mixer > 2110) { mixer = mixer - 1790; counter.value += 1; } else { mixer = mixer + 1170; }
    if (mixer > 2147) { mixer = mixer - 1843; counter.value += 1; } else { mixer = mixer + 1199; }
    if (mixer > 2184) { mixer = mixer - 1896; counter.value += 1; } else { mixer = mixer + 1228; }
    if (mixer > 2221) { mixer = mixer - 1949; counter.value += 1; } else { mixer = mixer + 1257; }
    if (mixer > 2258) { mixer = mixer - 2002; counter.value += 1; } else { mixer = mixer + 1286; }
    if (mixer > 2295) { mixer = mixer - 2055; counter.value += 1; } else { mixer = mixer + 1315; }
    if (mixer > 2332) { mixer = mixer - 2108; counter.value += 1; } else { mixer = mixer + 1344; }
    if (mixer > 2369) { mixer = mixer - 2161; counter.value += 1; } else { mixer = mixer + 1373; }
    if (mixer > 2406) { mixer = mixer - 208; counter.value += 1; } else { mixer = mixer + 1402; }
    if (mixer > 2443) { mixer = mixer - 224; counter.value += 1; } else { mixer = mixer + 1431; }
    if (mixer > 2480) { mixer = mixer - 240; counter.value += 1; } else { mixer = mixer + 1460; }
    if (mixer > 2517) { mixer = mixer - 256; counter.value += 1; } else { mixer = mixer + 1489; }
    if (mixer > 2554) { mixer = mixer - 272; counter.value += 1; } else { mixer = mixer + 1518; }
    if (mixer > 2591) { mixer = mixer - 288; counter.value += 1; } else { mixer = mixer + 1547; }
    if (mixer > 2628) { mixer = mixer - 304; counter.value += 1; } else { mixer = mixer + 1576; }
    if (mixer > 2665) { mixer = mixer - 320; counter.value += 1; } else { mixer = mixer + 1605; }
    if (mixer > 2702) { mixer = mixer - 336; counter.value += 1; } else { mixer = mixer + 1634; }
    if (mixer > 2739) { mixer = mixer - 352; counter.value += 1; } else { mixer = mixer + 1663; }
    if (mixer > 2776) { mixer = mixer - 368; counter.value += 1; } else { mixer = mixer + 1692; }
    if (mixer > 2813) { mixer = mixer - 384; counter.value += 1; } else { mixer = mixer + 1721; }
    if (mixer > 2850) { mixer = mixer - 400; counter.value += 1; } else { mixer = mixer + 1750; }
    if (mixer > 2887) { mixer = mixer - 416; counter.value += 1; } else { mixer = mixer + 1779; }
    if (mixer > 2924) { mixer = mixer - 432; counter.value += 1; } else { mixer = mixer + 1808; }
    if (mixer > 2961) { mixer = mixer - 448; counter.value += 1; } else { mixer = mixer + 1837; }
    if (mixer > 2998) { mixer = mixer - 464; counter.value += 1; } else { mixer = mixer + 1866; }
    if (mixer > 3035) { mixer = mixer - 480; counter.value += 1; } else { mixer = mixer + 1895; }
    if (mixer > 3072) { mixer = mixer - 496; counter.value += 1; } else { mixer = mixer + 1924; }
    if (mixer > 3109) { mixer = mixer - 512; counter.value += 1; } else { mixer = mixer + 1953; }
    if (mixer > 3146) { mixer = mixer - 528; counter.value += 1; } else { mixer = mixer + 1982; }
    if (mixer > 3183) { mixer = mixer - 544; counter.value += 1; } else { mixer = mixer + 2011; }
    if (mixer > 3220) { mixer = mixer - 560; counter.value += 1; } else { mixer = mixer + 2040; }
    if (mixer > 3257) { mixer = mixer - 576; counter.value += 1; } else { mixer = mixer + 2069; }
    if (mixer > 3294) { mixer = mixer - 592; counter.value += 1; } else { mixer = mixer + 2098; }
    if (mixer > 3331) { mixer = mixer - 608; counter.value += 1; } else { mixer = mixer + 2127; }
    if (mixer > 3368) { mixer = mixer - 624; counter.value += 1; } else { mixer = mixer + 2156; }
    if (mixer > 3405) { mixer = mixer - 640; counter.value += 1; } else { mixer = mixer + 2185; }
    if (mixer > 3442) { mixer = mixer - 656; counter.value += 1; } else { mixer = mixer + 2214; }
    if (mixer > 3479) { mixer = mixer - 672; counter.value += 1; } else { mixer = mixer + 2243; }
    if (mixer > 3516) { mixer = mixer - 688; counter.value += 1; } else { mixer = mixer + 2272; }
    if (mixer > 3553) { mixer = mixer - 704; counter.value += 1; } else { mixer = mixer + 2301; }
    if (mixer > 3590) { mixer = mixer - 720; counter.value += 1; } else { mixer = mixer + 2330; }
    if (mixer > 3627) { mixer = mixer - 736; counter.value += 1; } else { mixer = mixer + 2359; }
    if (mixer > 3664) { mixer = mixer - 752; counter.value += 1; } else { mixer = mixer + 2388; }
    if (mixer > 3701) { mixer = mixer - 768; counter.value += 1; } else { mixer = mixer + 2417; }
    if (mixer > 3738) { mixer = mixer - 784; counter.value += 1; } else { mixer = mixer + 2446; }
    if (mixer > 3775) { mixer = mixer - 800; counter.value += 1; } else { mixer = mixer + 2475; }
    if (mixer > 3812) { mixer = mixer - 816; counter.value += 1; } else { mixer = mixer + 2504; }
    if (mixer > 3849) { mixer = mixer - 832; counter.value += 1; } else { mixer = mixer + 2533; }
    if (mixer > 3886) { mixer = mixer - 848; counter.value += 1; } else { mixer = mixer + 2562; }
    if (mixer > 3923) { mixer = mixer - 864; counter.value += 1; } else { mixer = mixer + 2591; }
    if (mixer > 3960) { mixer = mixer - 880; counter.value += 1; } else { mixer = mixer + 2620; }
    if (mixer > 3997) { mixer = mixer - 896; counter.value += 1; } else { mixer = mixer + 2649; }
    if (mixer > 4034) { mixer = mixer - 912; counter.value += 1; } else { mixer = mixer + 2678; }
    if (mixer > 4071) { mixer = mixer - 928; counter.value += 1; } else { mixer = mixer + 2707; }
    if (mixer > 4108) { mixer = mixer - 944; counter.value += 1; } else { mixer = mixer + 2736; }
    if (mixer > 4145) { mixer = mixer - 960; counter.value += 1; } else { mixer = mixer + 2765; }
    if (mixer > 4182) { mixer = mixer - 976; counter.value += 1; } else { mixer = mixer + 2794; }
    if (mixer > 4219) { mixer = mixer - 992; counter.value += 1; } else { mixer = mixer + 2823; }
    if (mixer > 4256) { mixer = mixer - 1008; counter.value += 1; } else { mixer = mixer + 2852; }
    if (mixer > 4293) { mixer = mixer - 1024; counter.value += 1; } else { mixer = mixer + 2881; }
    if (mixer > 4330) { mixer = mixer - 1040; counter.value += 1; } else { mixer = mixer + 2910; }
    if (mixer > 4367) { mixer = mixer - 1056; counter.value += 1; } else { mixer = mixer + 2939; }
    if (mixer > 4404) { mixer = mixer - 1072; counter.value += 1; } else { mixer = mixer + 2968; }
    if (mixer > 4441) { mixer = mixer - 1088; counter.value += 1; } else { mixer = mixer + 2997; }
    if (mixer > 4478) { mixer = mixer - 1104; counter.value += 1; } else { mixer = mixer + 3026; }
    if (mixer > 4515) { mixer = mixer - 1120; counter.value += 1; } else { mixer = mixer + 3055; }
    if (mixer > 4552) { mixer = mixer - 1136; counter.value += 1; } else { mixer = mixer + 3084; }
    if (mixer > 4589) { mixer = mixer - 1152; counter.value += 1; } else { mixer = mixer + 3113; }
    if (mixer > 4626) { mixer = mixer - 1168; counter.value += 1; } else { mixer = mixer + 3142; }
    if (mixer > 4663) { mixer = mixer - 1184; counter.value += 1; } else { mixer = mixer + 3171; }
    if (mixer > 4700) { mixer = mixer - 1200; counter.value += 1; } else { mixer = mixer + 3200; }
    if (mixer > 4737) { mixer = mixer - 1216; counter.value += 1; } else { mixer = mixer + 3229; }
    if (mixer > 4774) { mixer = mixer - 1232; counter.value += 1; } else { mixer = mixer + 3258; }
    if (mixer > 4811) { mixer = mixer - 1248; counter.value += 1; } else { mixer = mixer + 3287; }
    if (mixer > 4848) { mixer = mixer - 1264; counter.value += 1; } else { mixer = mixer + 3316; }
    if (mixer > 4885) { mixer = mixer - 1280; counter.value += 1; } else { mixer = mixer + 3345; }
    if (mixer > 4922) { mixer = mixer - 1296; counter.value += 1; } else { mixer = mixer + 3374; }
    if (mixer > 4959) { mixer = mixer - 1312; counter.value += 1; } else { mixer = mixer + 3403; }
    if (mixer > 4996) { mixer = mixer - 1328; counter.value += 1; } else { mixer = mixer + 3432; }
    if (mixer > 5033) { mixer = mixer - 1344; counter.value += 1; } else { mixer = mixer + 3461; }
    if (mixer > 5070) { mixer = mixer - 1360; counter.value += 1; } else { mixer = mixer + 3490; }
    if (mixer > 5107) { mixer = mixer - 1376; counter.value += 1; } else { mixer = mixer + 3519; }
    if (mixer > 5144) { mixer = mixer - 1392; counter.value += 1; } else { mixer = mixer + 3548; }
    if (mixer > 5181) { mixer = mixer - 1408; counter.value += 1; } else { mixer = mixer + 3577; }
    if (mixer > 5218) { mixer = mixer - 1424; counter.value += 1; } else { mixer = mixer + 3606; }
    if (mixer > 5255) { mixer = mixer - 1440; counter.value += 1; } else { mixer = mixer + 3635; }
    if (mixer > 5292) { mixer = mixer - 1456; counter.value += 1; } else { mixer = mixer + 3664; }
    if (mixer > 5329) { mixer = mixer - 1472; counter.value += 1; } else { mixer = mixer + 3693; }
    if (mixer > 5366) { mixer = mixer - 1488; counter.value += 1; } else { mixer = mixer + 3722; }
    if (mixer > 5403) { mixer = mixer - 1504; counter.value += 1; } else { mixer = mixer + 3751; }
    if (mixer > 5440) { mixer = mixer - 1520; counter.value += 1; } else { mixer = mixer + 3780; }
    if (mixer > 5477) { mixer = mixer - 1536; counter.value += 1; } else { mixer = mixer + 3809; }
    if (mixer > 5514) { mixer = mixer - 1552; counter.value += 1; } else { mixer = mixer + 3838; }
    if (mixer > 5551) { mixer = mixer - 1568; counter.value += 1; } else { mixer = mixer + 3867; }
    if (mixer > 5588) { mixer = mixer - 1584; counter.value += 1; } else { mixer = mixer + 3896; }
    if (mixer > 5625) { mixer = mixer - 1600; counter.value += 1; } else { mixer = mixer + 3925; }
    if (mixer > 5662) { mixer = mixer - 1616; counter.value += 1; } else { mixer = mixer + 3954; }
    if (mixer > 5699) { mixer = mixer - 1632; counter.value += 1; } else { mixer = mixer + 3983; }
    if (mixer > 5736) { mixer = mixer - 1648; counter.value += 1; } else { mixer = mixer + 4012; }
    if (mixer > 5773) { mixer = mixer - 1664; counter.value += 1; } else { mixer = mixer + 4041; }
    if (mixer > 5810) { mixer = mixer - 1680; counter.value += 1; } else { mixer = mixer + 4070; }
    if (mixer > 5847) { mixer = mixer - 1696; counter.value += 1; } else { mixer = mixer + 4099; }
    if (mixer > 5884) { mixer = mixer - 1712; counter.value += 1; } else { mixer = mixer + 4128; }
    if (mixer > 5921) { mixer = mixer - 1728; counter.value += 1; } else { mixer = mixer + 4157; }
    if (mixer > 5958) { mixer = mixer - 1744; counter.value += 1; } else { mixer = mixer + 4186; }
    if (mixer > 5995) { mixer = mixer - 1760; counter.value += 1; } else { mixer = mixer + 4215; }
    if (mixer > 6032) { mixer = mixer - 1776; counter.value += 1; } else { mixer = mixer + 4244; }
    if (mixer > 6069) { mixer = mixer - 1792; counter.value += 1; } else { mixer = mixer + 4273; }
    if (mixer > 6106) { mixer = mixer - 1808; counter.value += 1; } else { mixer = mixer + 4302; }
    if (mixer > 6143) { mixer = mixer - 1824; counter.value += 1; } else { mixer = mixer + 4331; }
    if (mixer > 6180) { mixer = mixer - 1840; counter.value += 1; } else { mixer = mixer + 4360; }
    if (mixer > 6217) { mixer = mixer - 1856; counter.value += 1; } else { mixer = mixer + 4389; }
    if (mixer > 6254) { mixer = mixer - 1872; counter.value += 1; } else { mixer = mixer + 4418; }
    if (mixer > 6291) { mixer = mixer - 1888; counter.value += 1; } else { mixer = mixer + 4447; }
    if (mixer > 6328) { mixer = mixer - 1904; counter.value += 1; } else { mixer = mixer + 4476; }
    if (mixer > 6365) { mixer = mixer - 1920; counter.value += 1; } else { mixer = mixer + 4505; }
    if (mixer > 6402) { mixer = mixer - 1936; counter.value += 1; } else { mixer = mixer + 4534; }
    if (mixer > 6439) { mixer = mixer - 1952; counter.value += 1; } else { mixer = mixer + 4563; }
    if (mixer > 6476) { mixer = mixer - 1968; counter.value += 1; } else { mixer = mixer + 4592; }
    if (mixer > 6513) { mixer = mixer - 1984; counter.value += 1; } else { mixer = mixer + 4621; }
    if (mixer > 6550) { mixer = mixer - 2000; counter.value += 1; } else { mixer = mixer + 4650; }
    if (mixer > 6587) { mixer = mixer - 2016; counter.value += 1; } else { mixer = mixer + 4679; }
    if (mixer > 6624) { mixer = mixer - 2032; counter.value += 1; } else { mixer = mixer + 4708; }
    if (mixer > 6661) { mixer = mixer - 2048; counter.value += 1; } else { mixer = mixer + 4737; }
    if (mixer > 6698) { mixer = mixer - 2064; counter.value += 1; } else { mixer = mixer + 4766; }
    if (mixer > 6735) { mixer = mixer - 2080; counter.value += 1; } else { mixer = mixer + 4795; }
    if (mixer > 6772) { mixer = mixer - 2096; counter.value += 1; } else { mixer = mixer + 4824; }
    if (mixer > 6809) { mixer = mixer - 2112; counter.value += 1; } else { mixer = mixer + 4853; }
    if (mixer > 6846) { mixer = mixer - 2128; counter.value += 1; } else { mixer = mixer + 4882; }
    if (mixer > 6883) { mixer = mixer - 2144; counter.value += 1; } else { mixer = mixer + 4911; }
    if (mixer > 6920) { mixer = mixer - 2160; counter.value += 1; } else { mixer = mixer + 4940; }
    if (mixer > 6957) { mixer = mixer - 2176; counter.value += 1; } else { mixer = mixer + 4969; }
    if (mixer > 6994) { mixer = mixer - 2192; counter.value += 1; } else { mixer = mixer + 4998; }
    if (mixer > 7031) { mixer = mixer - 2208; counter.value += 1; } else { mixer = mixer + 5027; }
    if (mixer > 7068) { mixer = mixer - 2224; counter.value += 1; } else { mixer = mixer + 5056; }
    if (mixer > 7105) { mixer = mixer - 2240; counter.value += 1; } else { mixer = mixer + 5085; }
    if (mixer > 7142) { mixer = mixer - 2256; counter.value += 1; } else { mixer = mixer + 5114; }
    if (mixer > 7179) { mixer = mixer - 2272; counter.value += 1; } else { mixer = mixer + 5143; }
    if (mixer > 7216) { mixer = mixer - 2288; counter.value += 1; } else { mixer = mixer + 5172; }
    if (mixer > 7253) { mixer = mixer - 2304; counter.value += 1; } else { mixer = mixer + 5201; }
    if (mixer > 7290) { mixer = mixer - 2320; counter.value += 1; } else { mixer = mixer + 5230; }
    if (mixer > 7327) { mixer = mixer - 2336; counter.value += 1; } else { mixer = mixer + 5259; }
    if (mixer > 7364) { mixer = mixer - 2352; counter.value += 1; } else { mixer = mixer + 5288; }
    if (mixer > 7401) { mixer = mixer - 2368; counter.value += 1; } else { mixer = mixer + 5317; }
    if (mixer > 7438) { mixer = mixer - 2384; counter.value += 1; } else { mixer = mixer + 5346; }
    if (mixer > 7475) { mixer = mixer - 2400; counter.value += 1; } else { mixer = mixer + 5375; }
    if (mixer > 7512) { mixer = mixer - 2416; counter.value += 1; } else { mixer = mixer + 5404; }
    if (mixer > 7549) { mixer = mixer - 2432; counter.value += 1; } else { mixer = mixer + 5433; }
    if (mixer > 7586) { mixer = mixer - 2448; counter.value += 1; } else { mixer = mixer + 5462; }
    if (mixer > 7623) { mixer = mixer - 2464; counter.value += 1; } else { mixer = mixer + 5491; }
    if (mixer > 7660) { mixer = mixer - 2480; counter.value += 1; } else { mixer = mixer + 5520; }
    if (mixer > 7697) { mixer = mixer - 2496; counter.value += 1; } else { mixer = mixer + 5549; }
    if (mixer > 7734) { mixer = mixer - 2512; counter.value += 1; } else { mixer = mixer + 5578; }
    if (mixer > 7771) { mixer = mixer - 2528; counter.value += 1; } else { mixer = mixer + 5607; }
    if (mixer > 7808) { mixer = mixer - 2544; counter.value += 1; } else { mixer = mixer + 5636; }
    if (mixer > 7845) { mixer = mixer - 2560; counter.value += 1; } else { mixer = mixer + 5665; }
    if (mixer > 7882) { mixer = mixer - 2576; counter.value += 1; } else { mixer = mixer + 5694; }
    if (mixer > 7919) { mixer = mixer - 2592; counter.value += 1; } else { mixer = mixer + 5723; }
    if (mixer > 7956) { mixer = mixer - 2608; counter.value += 1; } else { mixer = mixer + 5752; }
    if (mixer > 7993) { mixer = mixer - 2624; counter.value += 1; } else { mixer = mixer + 5781; }
    if (mixer > 8030) { mixer = mixer - 2640; counter.value += 1; } else { mixer = mixer + 5810; }
    if (mixer > 8067) { mixer = mixer - 2656; counter.value += 1; } else { mixer = mixer + 5839; }
    if (mixer > 8104) { mixer = mixer - 2672; counter.value += 1; } else { mixer = mixer + 5868; }
    if (mixer > 8141) { mixer = mixer - 2688; counter.value += 1; } else { mixer = mixer + 5897; }
    if (mixer > 8178) { mixer = mixer - 2704; counter.value += 1; } else { mixer = mixer + 5926; }
    if (mixer > 8215) { mixer = mixer - 2720; counter.value += 1; } else { mixer = mixer + 5955; }
    if (mixer > 8252) { mixer = mixer - 2736; counter.value += 1; } else { mixer = mixer + 5984; }
    if (mixer > 8289) { mixer = mixer - 2752; counter.value += 1; } else { mixer = mixer + 6013; }
    if (mixer > 8326) { mixer = mixer - 2768; counter.value += 1; } else { mixer = mixer + 6042; }
    if (mixer > 8363) { mixer = mixer - 2784; counter.value += 1; } else { mixer = mixer + 6071; }
    if (mixer > 8400) { mixer = mixer - 2800; counter.value += 1; } else { mixer = mixer + 6100; }
    if (mixer > 8437) { mixer = mixer - 2816; counter.value += 1; } else { mixer = mixer + 6129; }
    if (mixer > 8474) { mixer = mixer - 2832; counter.value += 1; } else { mixer = mixer + 6158; }
    if (mixer > 8511) { mixer = mixer - 2848; counter.value += 1; } else { mixer = mixer + 6187; }
    if (mixer > 8548) { mixer = mixer - 2864; counter.value += 1; } else { mixer = mixer + 6216; }
    if (mixer > 8585) { mixer = mixer - 2880; counter.value += 1; } else { mixer = mixer + 6245; }
    if (mixer > 8622) { mixer = mixer - 2896; counter.value += 1; } else { mixer = mixer + 6274; }
    if (mixer > 8659) { mixer = mixer - 2912; counter.value += 1; } else { mixer = mixer + 6303; }
    if (mixer > 8696) { mixer = mixer - 2928; counter.value += 1; } else { mixer = mixer + 6332; }
    if (mixer > 8733) { mixer = mixer - 2944; counter.value += 1; } else { mixer = mixer + 6361; }
    if (mixer > 8770) { mixer = mixer - 2960; counter.value += 1; } else { mixer = mixer + 6390; }
    if (mixer > 8807) { mixer = mixer - 2976; counter.value += 1; } else { mixer = mixer + 6419; }
    if (mixer > 8844) { mixer = mixer - 2992; counter.value += 1; } else { mixer = mixer + 6448; }
    if (mixer > 8881) { mixer = mixer - 3008; counter.value += 1; } else { mixer = mixer + 6477; }
    if (mixer > 8918) { mixer = mixer - 3024; counter.value += 1; } else { mixer = mixer + 6506; }
    if (mixer > 8955) { mixer = mixer - 3040; counter.value += 1; } else { mixer = mixer + 6535; }
    if (mixer > 8992) { mixer = mixer - 3056; counter.value += 1; } else { mixer = mixer + 6564; }
    if (mixer > 9029) { mixer = mixer - 3072; counter.value += 1; } else { mixer = mixer + 6593; }
    if (mixer > 9066) { mixer = mixer - 3088; counter.value += 1; } else { mixer = mixer + 6622; }
    if (mixer > 9103) { mixer = mixer - 3104; counter.value += 1; } else { mixer = mixer + 6651; }
    if (mixer > 9140) { mixer = mixer - 3120; counter.value += 1; } else { mixer = mixer + 6680; }
    if (mixer > 9177) { mixer = mixer - 3136; counter.value += 1; } else { mixer = mixer + 6709; }
    if (mixer > 9214) { mixer = mixer - 3152; counter.value += 1; } else { mixer = mixer + 6738; }
    if (mixer > 9251) { mixer = mixer - 3168; counter.value += 1; } else { mixer = mixer + 6767; }
    if (mixer > 9288) { mixer = mixer - 3184; counter.value += 1; } else { mixer = mixer + 6796; }
    if (mixer > 9325) { mixer = mixer - 3200; counter.value += 1; } else { mixer = mixer + 6825; }
    if (mixer > 9362) { mixer = mixer - 3216; counter.value += 1; } else { mixer = mixer + 6854; }
    if (mixer > 9399) { mixer = mixer - 3232; counter.value += 1; } else { mixer = mixer + 6883; }
    if (mixer > 9436) { mixer = mixer - 3248; counter.value += 1; } else { mixer = mixer + 6912; }
    if (mixer > 9473) { mixer = mixer - 3264; counter.value += 1; } else { mixer = mixer + 6941; }
    if (mixer > 9510) { mixer = mixer - 3280; counter.value += 1; } else { mixer = mixer + 6970; }
    if (mixer > 9547) { mixer = mixer - 3296; counter.value += 1; } else { mixer = mixer + 6999; }
    if (mixer > 9584) { mixer = mixer - 3312; counter.value += 1; } else { mixer = mixer + 7028; }
    if (mixer > 9621) { mixer = mixer - 3328; counter.value += 1; } else { mixer = mixer + 7057; }
    if (mixer > 9658) { mixer = mixer - 3344; counter.value += 1; } else { mixer = mixer + 7086; }
    if (mixer > 9695) { mixer = mixer - 3360; counter.value += 1; } else { mixer = mixer + 7115; }
    if (mixer > 9732) { mixer = mixer - 3376; counter.value += 1; } else { mixer = mixer + 7144; }
    if (mixer > 9769) { mixer = mixer - 3392; counter.value += 1; } else { mixer = mixer + 7173; }
    if (mixer > 9806) { mixer = mixer - 3408; counter.value += 1; } else { mixer = mixer + 7202; }
    if (mixer > 9843) { mixer = mixer - 3424; counter.value += 1; } else { mixer = mixer + 7231; }
    if (mixer > 9880) { mixer = mixer - 3440; counter.value += 1; } else { mixer = mixer + 7260; }
    if (mixer > 9917) { mixer = mixer - 3456; counter.value += 1; } else { mixer = mixer + 7289; }
    if (mixer > 9954) { mixer = mixer - 3472; counter.value += 1; } else { mixer = mixer + 7318; }
    if (mixer > 9991) { mixer = mixer - 3488; counter.value += 1; } else { mixer = mixer + 7347; }
    if (mixer > 10028) { mixer = mixer - 3504; counter.value += 1; } else { mixer = mixer + 7376; }
    if (mixer > 10065) { mixer = mixer - 3520; counter.value += 1; } else { mixer = mixer + 7405; }
    if (mixer > 10102) { mixer = mixer - 3536; counter.value += 1; } else { mixer = mixer + 7434; }
    if (mixer > 10139) { mixer = mixer - 3552; counter.value += 1; } else { mixer = mixer + 7463; }
    if (mixer > 10176) { mixer = mixer - 3568; counter.value += 1; } else { mixer = mixer + 7492; }
    if (mixer > 10213) { mixer = mixer - 3584; counter.value += 1; } else { mixer = mixer + 7521; }
    if (mixer > 10250) { mixer = mixer - 3600; counter.value += 1; } else { mixer = mixer + 7550; }
    if (mixer > 10287) { mixer = mixer - 3616; counter.value += 1; } else { mixer = mixer + 7579; }
    if (mixer > 10324) { mixer = mixer - 3632; counter.value += 1; } else { mixer = mixer + 7608; }
    if (mixer > 10361) { mixer = mixer - 3648; counter.value += 1; } else { mixer = mixer + 7637; }
    if (mixer > 10398) { mixer = mixer - 3664; counter.value += 1; } else { mixer = mixer + 7666; }
    if (mixer > 10435) { mixer = mixer - 3680; counter.value += 1; } else { mixer = mixer + 7695; }
    if (mixer > 10472) { mixer = mixer - 3696; counter.value += 1; } else { mixer = mixer + 7724; }
    if (mixer > 10509) { mixer = mixer - 3712; counter.value += 1; } else { mixer = mixer + 7753; }
    if (mixer > 10546) { mixer = mixer - 3728; counter.value += 1; } else { mixer = mixer + 7782; }
    if (mixer > 10583) { mixer = mixer - 3744; counter.value += 1; } else { mixer = mixer + 7811; }
}
