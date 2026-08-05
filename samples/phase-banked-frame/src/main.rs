// Candidate build: the same hot frame loop as the control, preceded by bulky one-shot
// level preparation. The cold init phase pushes the movable program past fixed PRG, so the
// normal final-link ladder selects `nes-mmc3-tvrom-codebank-v1` and the phase placement
// policy has to keep `program:main:frame` whole inside one R6 bank.

// The fold stream below is generated bulk: 80 distinct branch folds over a running
// u16 mixer. Every line carries its own constants, so no shared body can absorb it and the
// canary keeps the program size it needs no matter how the target outlines user functions.
// Final state after the stream: mixer == 6189, counter.value == 46.

class Counter
{
    u8 value;
}

void PrepareLevel(Counter counter)
{
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
}

void Main()
{
    Scene scene;
    Counter counter;

    SetupScene();
    scene.Reset();
    counter.value = 0;
    PrepareLevel(counter);

    while (true)
    {
        StepFrame(scene);
        if (scene.ReachedGoal())
        {
            break;
        }
    }

    CompleteLevel(scene);
}
