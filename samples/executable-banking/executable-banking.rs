class Counter
{
    u8 value;

    inline void Step8()
    {
        value += 1;
        value += 1;
        value += 1;
        value += 1;
        value += 1;
        value += 1;
        value += 1;
        value += 1;
    }

    inline void Step64()
    {
        Step8();
        Step8();
        Step8();
        Step8();
        Step8();
        Step8();
        Step8();
        Step8();
    }

    inline void Step512()
    {
        Step64();
        Step64();
        Step64();
        Step64();
        Step64();
        Step64();
        Step64();
        Step64();
    }
}

void Main()
{
    Counter counter;
    counter.Step512();
    counter.Step512();
    counter.Step512();
    counter.Step512();
    counter.Step512();
    counter.Step512();
    counter.Step512();
    counter.Step512();
    counter.Step64();
    counter.Step64();
    counter.Step64();
    counter.Step64();
    counter.Step64();
    counter.Step64();
    counter.Step8();
    counter.Step8();
    counter.value += 1;
    counter.value += 1;
    counter.value += 1;
    counter.value += 1;
}
