cmake_minimum_required(VERSION 3.26)
project(vnsl C)

file(GLOB_RECURSE SOURCES src/*.c)
file(GLOB HEADERS *.h)

add_executable(vnsl ${SOURCES} ${HEADERS})
target_compile_features(vnsl PRIVATE c_std_23 cxx_std_26)
target_precompile_headers(vnsl PRIVATE pch.h)
set_target_properties(vnsl PROPERTIES INTERPROCEDURAL_OPTIMIZATION TRUE)